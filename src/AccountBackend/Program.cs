using System.Net;
using System.Net.Sockets;
using AccountBackend;

var configPath=Path.Combine(AppContext.BaseDirectory,"server.json"); string? hostOverride=null; int? portOverride=null;
for(int i=0;i<args.Length;i++) {
    if(i+1>=args.Length) throw new ArgumentException("Expected value after "+args[i]);
    switch(args[i]) { case "--config": configPath=Path.GetFullPath(args[++i]); break; case "--host": hostOverride=args[++i]; break; case "--port": portOverride=int.Parse(args[++i]); break; default: throw new ArgumentException("Unknown argument: "+args[i]); }
}
var config=J.Parse(File.ReadAllText(configPath)); var root=Path.GetDirectoryName(Path.GetFullPath(configPath))!;
var data=Path.GetFullPath(Path.Combine(root,config.S("catalog_directory","data"))); var path=Path.GetFullPath(Path.Combine(root,config.S("database","data/accounts.sqlite3")));
var initial=config["new_account"]; var port=portOverride??(int)config.I("listen_port",10118); J.Need(port is >=1 and <=65535,"Invalid port");
using var db=new Store(path,checked((int)initial.I("coins",10000000)),checked((int)initial.I("diamonds",10000)),data); db.LoadCatalog(data);
var router=new Router(db); var listener=new TcpListener(IPAddress.Parse(hostOverride??config.S("listen_host","127.0.0.1")),port); listener.Start(128);
using var stop=new CancellationTokenSource(); Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();listener.Stop();};
Console.WriteLine($"Account backend 10.18 listening on {listener.LocalEndpoint}; multiplayer disabled");
using var slots=new SemaphoreSlim(128); var clients=new List<Task>();
try { while(!stop.IsCancellationRequested) { await slots.WaitAsync(stop.Token); TcpClient client; try { client=await listener.AcceptTcpClientAsync(stop.Token); } catch { slots.Release(); throw; }
    var task=Run(client); clients.RemoveAll(t=>t.IsCompleted); clients.Add(task);
} } catch(Exception e) when(e is OperationCanceledException or SocketException && stop.IsCancellationRequested) { }
finally { stop.Cancel(); listener.Stop(); await Task.WhenAll(clients); }
async Task Run(TcpClient client) { try { await Transport.Client(client,router,stop.Token); } finally { slots.Release(); } }
