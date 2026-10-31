using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json.Linq;
using static AccountBackend.J;

namespace AccountBackend;

public static class Transport
{
    public const int MaxFrame=1024*1024;
    static readonly UTF8Encoding Utf8=new(false,true);
    static string ReadString(BinaryReader reader) { var size=reader.ReadInt32(); Need(size>=0 && size<=MaxFrame && size<=reader.BaseStream.Length-reader.BaseStream.Position,"Invalid string length"); return Utf8.GetString(reader.ReadBytes(size)); }
    static void WriteString(BinaryWriter writer,string text) { var bytes=Utf8.GetBytes(text); writer.Write(bytes.Length); writer.Write(bytes); }
    public static byte[] Response(uint index,Reply reply) {
        using var stream=new MemoryStream(); using var w=new BinaryWriter(stream,Utf8,true); w.Write((short)15777); w.Write(index); w.Write(reply.Code); WriteString(w,reply.Message);
        if(reply.Body==null) WriteString(w,"none"); else { var text=reply.Body.Type==JTokenType.String; WriteString(w,text?"text":"json"); WriteString(w,text?reply.Body.Value<string>()??"":Dump(reply.Body)); } return stream.ToArray();
    }
    public static async Task<byte[]> ReadFrame(Stream stream,CancellationToken token) {
        var header=new byte[4]; await stream.ReadExactlyAsync(header,token); var size=BinaryPrimitives.ReadInt32BigEndian(header); Need(size>=1 && size<=MaxFrame,"Invalid frame length");
        var result=new byte[size]; await stream.ReadExactlyAsync(result,token); return result;
    }
    static async Task Send(Stream stream,byte[] body,CancellationToken token) { var size=new byte[4]; BinaryPrimitives.WriteInt32BigEndian(size,body.Length); await stream.WriteAsync(size,token); await stream.WriteAsync(body,token); }
    public static async Task Client(TcpClient client,Router router,CancellationToken stopping) {
        using(client) try {
            client.NoDelay=true; using var stream=client.GetStream();
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(stopping)) {
                deadline.CancelAfter(TimeSpan.FromSeconds(15)); var hello=await ReadFrame(stream,deadline.Token); using var r=new BinaryReader(new MemoryStream(hello),Utf8);
                Need(r.ReadInt32()==8,"Unsupported transport hello"); ReadString(r); ReadString(r); Need(r.BaseStream.Position==r.BaseStream.Length,"Invalid transport hello"); await Send(stream,new byte[4],deadline.Token);
            }
            while(!stopping.IsCancellationRequested) {
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(stopping); deadline.CancelAfter(TimeSpan.FromSeconds(60)); var frame=await ReadFrame(stream,deadline.Token);
                using var r=new BinaryReader(new MemoryStream(frame),Utf8); Need(r.ReadInt16()==8,"Unsupported request layout"); var index=r.ReadUInt32(); var route=ReadString(r); r.ReadByte(); var type=ReadString(r); var body=type is "json" or "text"?ReadString(r):""; Need(r.BaseStream.Position==r.BaseStream.Length,"Invalid request layout");
                Reply reply; try { reply=router.Dispatch(route,type,body,index); } catch(Exception e) { Console.Error.WriteLine($"{DateTimeOffset.UtcNow:O} handler_error {e.GetType().Name}"); reply=new(500,"Internal server error"); }
                if(reply.Code!=0) { Console.WriteLine($"{DateTimeOffset.UtcNow:O} {route} code={reply.Code} {reply.Message}"); await Task.Delay(250,deadline.Token); }
                await Send(stream,Response(index,reply),deadline.Token);
            }
        } catch(Exception e) when(e is IOException or RequestError or OperationCanceledException or DecoderFallbackException or SocketException) { }
    }
}
