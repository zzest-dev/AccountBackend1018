using AccountBackend;
using Newtonsoft.Json.Linq;
using static AccountBackend.J;

var data=Path.GetFullPath(args[0]);
if(args.Length>1 && args[1]=="--differential") {
    string? line; while((line=Console.ReadLine())!=null) {
        var input=Parse(line); Clock=new FixedClock(input.D("time"));
        using var db=new Store(input.S("database"),10000000,10000,data);
        try {
            JToken result=input.S("operation") switch {
                "commerce"=>new Commerce(db).Purchase(input.I("user"),input.S("key"),(JObject)input["request"]!),
                "mission"=>new Missions(db).Process(input.I("user"),input.S("route"),input.S("key"),(JObject)input["request"]!),
                _=>throw new Exception("Unknown differential operation")
            };
            Console.WriteLine(Dump(new JObject { ["result"]=result,["state"]=Snapshot(db) }));
        } catch(RequestError e) { Console.WriteLine(Dump(new JObject { ["error"]=e.Message,["state"]=Snapshot(db) })); }
    }
    return;
}
using(var db=new Store(":memory:",10000000,10000,data)) {
    db.LoadCatalog(data); var router=new Router(db); uint index=0;
    Reply Call(string route,JObject value)=>router.Dispatch(route,"json",Dump(value),++index);
    void Check(bool value,string name) { if(!value) throw new Exception(name); Console.WriteLine("PASS "+name); }
    var connect=(JObject)Call("/main/open_connect/",new JObject { ["hand_shake"]=Guid.NewGuid().ToString() }).Body!;
    var account=(JObject)Call("/main/register/",new JObject { ["connect_id"]=connect["connect_id"],["reg_token"]="FKDJfjLSdhHfKJDS",["vProtocol"]=2 }).Body!;
    var login=(JObject)Call("/main/login/",new JObject { ["connect_id"]=connect["connect_id"],["token"]=account["token"] }).Body!;
    var token=login.S("session_token"); var auth=new JObject { ["session_token"]=token };
    Check(Call("/user/get_cloud_data/",auth).Body!["User"]!.S("nik")=="user 1","account default nickname");
    Check(Call("/user/get_cloud_data/",new JObject { ["session_token"]="fake" }).Code==401,"unauthorized rejected");
    var buy=new JObject { ["session_token"]=token,["version"]=1,["items"]=new JArray(new JObject { ["strID"]="Golf" }) };
    var first=router.Dispatch("/sell/buy_items/","json",Dump(buy),100); var retry=router.Dispatch("/sell/buy_items/","json",Dump(buy),100);
    Check(first.Code==0 && Same(first.Body,retry.Body) && db.Account(1).I("diamonds")==9970,"atomic purchase and replay");
    var mp=new JObject { ["session_token"]=token,["user_id"]=1 }; Check(((JArray)Call("/match/list/",mp).Body!["items"]!).Count==0,"multiplayer list empty"); Check(Call("/match/try_connect_to_game_server/",mp).Code==503,"multiplayer connect refused");
    Check(Call("/game/validate_session/",auth).Code==503,"android game server admission disabled");
    var bytes=Transport.Response(7,new Reply(0,"",new JValue("ok_begin"))); using var reader=new BinaryReader(new MemoryStream(bytes)); Check(reader.ReadInt16()==15777 && reader.ReadUInt32()==7 && reader.ReadInt16()==0,"binary response header");
    try { new UnimplementedGamePacketBridge().MirrorToUnet(new(0,ReadOnlyMemory<byte>.Empty)); throw new Exception("bridge unexpectedly implemented"); } catch(NotSupportedException) { Console.WriteLine("PASS bridge stub explicit failure"); }
    Check(db.Query("PRAGMA integrity_check")[0].Properties().First().Value.ToString()=="ok","sqlite integrity");
}
Console.WriteLine("ALL TESTS PASS");
static JObject Snapshot(Store db) => new() { ["accounts"]=new JArray(db.Query("SELECT id,nickname,coins,diamonds,revision FROM accounts ORDER BY id")),["inventory"]=new JArray(db.Query("SELECT * FROM inventory ORDER BY account_id,str_id")),["profiles"]=new JArray(db.Query("SELECT * FROM saved_profiles ORDER BY account_id,block,item_key")),["progress"]=new JArray(db.Query("SELECT * FROM mission_progress ORDER BY account_id,mission_name")) };
sealed class FixedClock(double seconds) : TimeProvider { public override DateTimeOffset GetUtcNow()=>DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds*1000)); }
