using System.Buffers.Binary;
using System.Text;

namespace PhotoSiftReborn;

/// <summary>
/// Read-only, bounded image-header inspection for the everyday photo inspector.
/// Never parses or displays embedded GPS coordinates; never writes to images.
/// The metadata is not a substitute for a native file-ownership approval.
/// </summary>
public sealed record PhotoTechnicalMetadata(
    string Format, int? Width, int? Height, int? ExifOrientation,
    string? CameraMake, string? CameraModel, string? DateTimeOriginal,
    string Note)
{
    public string Description =>
        $"Format: {Format} · Encoded dimensions: " + (Width is > 0 && Height is > 0 ? $"{Width} × {Height} px" : "not available") +
        $" · EXIF orientation: {(ExifOrientation is >= 1 and <= 8 ? ExifOrientation.Value.ToString() : "not recorded")}" +
        $"\nCamera: {(string.IsNullOrWhiteSpace(CameraMake) && string.IsNullOrWhiteSpace(CameraModel) ? "not recorded" : (CameraMake + " " + CameraModel).Trim())}" +
        $" · Camera-local date (zone unknown): {DateTimeOriginal ?? "not recorded"}" +
        $"\n{Note} No GPS/location fields are displayed. Source image is unchanged.";
}

public static class PhotoTechnicalMetadataReader
{
    public const int HeaderLimit = 1024 * 1024;

    /// <summary>Inspect a saved original only when its imported metadata and optional
    /// native file identity still match before and after the bounded read.</summary>
    public static PhotoTechnicalMetadata Inspect(Photo photo)
    {
        if (photo.Id == Guid.Empty || !Path.IsPathFullyQualified(photo.Path) ||
            SortingWorkspace.HasLinkedExistingAncestor(Path.GetDirectoryName(photo.Path)!) ||
            SortingWorkspace.HasSymlinkOrReparsePoint(photo.Path))
            throw new IOException("Photo inspection refused: linked, unqualified or invalid original path.");
        void Check()
        {
            var file = new FileInfo(photo.Path);
            if (!file.Exists || file.Length != photo.Length ||
                file.LastWriteTimeUtc.Ticks != photo.LastWriteUtcTicks ||
                (photo.NativeIdentity is not null &&
                 (!NativeFileCapability.IsSupported || NativeFileCapability.Identity(photo.Path) != photo.NativeIdentity)))
                throw new IOException("The original photograph changed since import. Metadata inspection refused.");
        }
        Check();
        byte[] buffer = new byte[HeaderLimit];
        int used = 0;
        using (var stream = new FileStream(photo.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            while (used < buffer.Length)
            {
                int read = stream.Read(buffer, used, buffer.Length - used);
                if (read == 0) break;
                used += read;
            }
        }
        Check();
        var result=Parse(buffer.AsSpan(0, used));
        var extension=Path.GetExtension(photo.Path).ToLowerInvariant();
        bool agrees=result.Format switch
        {
            "JPEG" => extension is ".jpg" or ".jpeg",
            "PNG" => extension==".png",
            "GIF" => extension==".gif",
            "BMP" => extension==".bmp",
            _ => false
        };
        if(!agrees)
            throw new InvalidDataException("Photograph content header and filename extension disagree. Original preserved.");
        return result;
    }

    /// <summary>Pure bounded-header parser; intentionally safe on truncated/malformed EXIF.
    /// No decoder delegates, filesystem writes or metadata expansion.</summary>
    public static PhotoTechnicalMetadata Parse(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
        {
            if (header.Length < 24 || !header.Slice(12,4).SequenceEqual("IHDR"u8))
                return new("PNG",null,null,null,null,null,null,"PNG header is incomplete.");
            uint w=BinaryPrimitives.ReadUInt32BigEndian(header.Slice(16,4));
            uint h=BinaryPrimitives.ReadUInt32BigEndian(header.Slice(20,4));
            return new("PNG",Valid(w),Valid(h),null,null,null,null,"PNG structural header (not a full file validation).");
        }
        if (header.Length >= 10 &&
            (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)))
            return new("GIF",BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6,2)),
                BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(8,2)),null,null,null,null,
                "GIF logical-screen dimensions (frame count not inferred).");
        if (header.Length >= 26 && header[0]==0x42 && header[1]==0x4D)
        {
            int w=BinaryPrimitives.ReadInt32LittleEndian(header.Slice(18,4));
            int h=BinaryPrimitives.ReadInt32LittleEndian(header.Slice(22,4));
            return new("BMP",w>0?w:null,h!=0 && h!=int.MinValue ? Math.Abs(h) : null,
                null,null,null,null,"BMP structural header (not a full file validation).");
        }
        if (header.Length >= 3 && header[0]==255 && header[1]==216 && header[2]==255)
            return ParseJpeg(header);
        return new("Unsupported/unknown",null,null,null,null,null,null,
            "Header does not match a supported JPEG, PNG, GIF or BMP signature.");
    }

    private static int? Valid(uint n) => n is >0 and <=1000000 ? (int)n : null;

    private static PhotoTechnicalMetadata ParseJpeg(ReadOnlySpan<byte> b)
    {
        int? width=null,height=null,orientation=null;
        string? make=null,model=null,taken=null;
        int i=2;
        int scanned=0;
        bool incomplete=false;
        while (i+3 < b.Length && scanned++ < 4096)
        {
            if (b[i++] != 0xFF) { incomplete=true; break; }
            while (i<b.Length && b[i]==0xFF) i++;
            if (i>=b.Length) { incomplete=true;break; }
            byte marker=b[i++];
            if (marker is 0xD9 or 0xDA) break;
            if (marker is 0x01 or >=0xD0 and <=0xD8) continue;
            if (i+2>b.Length) {incomplete=true;break;}
            int len=BinaryPrimitives.ReadUInt16BigEndian(b.Slice(i,2));
            if (len<2 || (long)i+len>b.Length) {incomplete=true;break;}
            var content=b.Slice(i+2,len-2);
            if (marker==0xE1 && content.Length>14 && content[..6].SequenceEqual("Exif\0\0"u8))
                ParseExif(content[6..],ref orientation,ref make,ref model,ref taken);
            if (marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF)
            {
                if (content.Length>=5)
                {
                    height=BinaryPrimitives.ReadUInt16BigEndian(content.Slice(1,2));
                    width=BinaryPrimitives.ReadUInt16BigEndian(content.Slice(3,2));
                }
            }
            i+=len;
        }
        return new("JPEG",width,height,orientation,make,model,taken,
            incomplete ? "Header is truncated or malformed; incomplete fields are intentionally omitted." :
            "Read-only JPEG markers and bounded EXIF inspection; unverified tags are omitted.");
    }

    private static ushort Read16(ReadOnlySpan<byte> data,int offset,bool little)
    {
        if(offset<0 || offset>data.Length-2)return 0;
        return little ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset,2))
                      : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset,2));
    }
    private static uint Read32(ReadOnlySpan<byte> data,int offset,bool little)
    {
        if(offset<0 || offset>data.Length-4)return 0;
        return little ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset,4))
                      : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset,4));
    }
    private static string? ReadAscii(ReadOnlySpan<byte> data,int entry,bool little)
    {
        if(Read16(data,entry+2,little)!=2)return null;
        uint length=Read32(data,entry+4,little);
        if(length<2 || length>128)return null;
        uint offset=length<=4 ? (uint)(entry+8) : Read32(data,entry+8,little);
        if(offset>(uint)data.Length || length>(uint)data.Length-offset)return null;
        var bytes=data.Slice((int)offset,(int)length);
        if(bytes[^1]!=0)return null;
        var payload=bytes[..^1];
        foreach(byte c in payload) if(c<32 || c>126)return null;
        return Encoding.ASCII.GetString(payload);
    }
    private static void ParseExif(ReadOnlySpan<byte> t,ref int? orientation,ref string? make,
        ref string? model,ref string? taken)
    {
        if(t.Length<8)return;
        bool little=t[..2].SequenceEqual("II"u8);
        if(!little && !t[..2].SequenceEqual("MM"u8))return;
        if(Read16(t,2,little)!=42)return;
        var offset=Read32(t,4,little);
        if(offset>(uint)t.Length-2)return;
        int start=(int)offset;
        int count=Math.Min((int)Read16(t,start,little),128);
        if((long)start+2L+12L*count>t.Length)return;
        uint exifOffset=0;
        for(int j=0;j<count;j++)
        {
            int e=start+2+12*j;
            ushort tag=Read16(t,e,little);
            if(tag==0x0112 && Read16(t,e+2,little)==3 && Read32(t,e+4,little)==1)
            {int value=Read16(t,e+8,little);if(value is >=1 and <=8)orientation=value;}
            else if(tag==0x010F)make=ReadAscii(t,e,little);
            else if(tag==0x0110)model=ReadAscii(t,e,little);
            else if(tag==0x8769 && Read16(t,e+2,little)==4 && Read32(t,e+4,little)==1)
                exifOffset=Read32(t,e+8,little);
        }
        if(exifOffset>(uint)t.Length-2)return;
        start=(int)exifOffset;
        count=Math.Min((int)Read16(t,start,little),128);
        if((long)start+2L+12L*count>t.Length)return;
        for(int j=0;j<count;j++)
        {
            int e=start+2+12*j;
            if(Read16(t,e,little)!=0x9003)continue;
            var raw=ReadAscii(t,e,little);
            if(raw is {Length:19} && raw[4]==':' && raw[7]==':' && raw[10]==' ' &&
                raw[13]==':' && raw[16]==':' &&
                raw.Where((c,index)=>index is not (4 or 7 or 10 or 13 or 16)).All(char.IsDigit))
                taken=raw;
        }
    }
}
