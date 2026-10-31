using Newtonsoft.Json.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static AccountBackend.J;

namespace AccountBackend;

public sealed class Store : IDisposable
{
    readonly SqliteDb connection;
    readonly int coins, diamonds;
    public Store(string path, int initialCoins, int initialDiamonds, string data)
    {
        Need(initialCoins >= 0 && initialDiamonds >= 0, "invalid_initial_balance");
        coins=initialCoins; diamonds=initialDiamonds;
        if(path != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connection = new SqliteDb(path);
        Exec("PRAGMA foreign_keys=ON"); Exec("PRAGMA journal_mode=WAL"); Exec(File.ReadAllText(Path.Combine(data,"schema.sql")));
        if(!Query("PRAGMA table_info(accounts)").Any(x=>x.S("name")=="revision")) Exec("ALTER TABLE accounts ADD COLUMN revision INTEGER NOT NULL DEFAULT 1");
        Exec("UPDATE accounts SET nickname='user ' || id,revision=revision+1 WHERE nickname='Player'");
    }
    public int Exec(string sql, params object?[] args) => connection.Exec(sql,args);
    public List<JObject> Query(string sql, params object?[] args) => connection.Query(sql,args);
    public JObject? One(string sql, params object?[] args) => Query(sql,args).FirstOrDefault();
    public long LastId => One("SELECT last_insert_rowid() AS id")!.I("id");
    public T Atomic<T>(Func<T> action) { Exec("BEGIN IMMEDIATE"); try { var value=action(); Exec("COMMIT"); return value; } catch { Exec("ROLLBACK"); throw; } }
    public JObject Account(long id) => One("SELECT * FROM accounts WHERE id=?",id) ?? throw new RequestError("account_not_found");
    public JObject Register() => Atomic(()=> {
        var guid=Guid.NewGuid().ToString(); var token=Random(); var now=Now;
        Exec("INSERT INTO accounts(account_key,nickname,coins,diamonds,created_at) VALUES(?,?,?,?,?)","guest:"+guid,"Player",coins,diamonds,now); var id=LastId;
        Exec("UPDATE accounts SET nickname=? WHERE id=?","user "+id,id);
        Exec("INSERT INTO credentials(account_id,guid,token_hash) VALUES(?,?,?)",id,guid,Hash(token));
        return new JObject { ["user_id"]=id,["guid"]=guid,["token"]=token,["timeCreatedUtcLinux"]=now,["lastTimePlayTime"]=0 };
    });
    public JObject? Authenticate(string token) => token.Length is >0 and <=4096 ? One("SELECT accounts.*,credentials.guid FROM accounts JOIN credentials ON accounts.id=credentials.account_id WHERE credentials.token_hash=?",Hash(token)):null;
    public JObject Nickname(long id, JToken? name, bool apply) => Atomic(()=> {
        var key=apply?"assigned":"is_available"; var n=name?.Type==JTokenType.String?name.Value<string>()??"":"";
        var reason="";
        if(n.Length is <3 or >24 || n!=n.Trim() || !n.Any(char.IsLetterOrDigit) || !n.All(c=>char.IsLetterOrDigit(c)||" _.-".Contains(c))) reason="Use 3-24 letters, numbers, spaces, _, . or -";
        var normalized=n.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        if(reason=="" && Regex.IsMatch(normalized,@"^USER\s*\d+$") && normalized!="USER "+id) reason="Nickname is reserved";
        if(reason=="" && Query("SELECT nickname FROM accounts WHERE id<>?",id).Any(x=>x.S("nickname").Normalize(NormalizationForm.FormKC).ToUpperInvariant()==normalized)) reason="Nickname is already taken";
        if(reason=="" && apply && Account(id).S("nickname")!=n) Exec("UPDATE accounts SET nickname=?,revision=revision+1 WHERE id=?",n,id);
        return new JObject { [key]=reason=="",["reason"]=reason };
    });
    public JObject Stats(long id,JObject request) => Atomic(()=> {
        var guid=request.S("packet_guid"); Need(Regex.IsMatch(guid,@"^[A-Za-z0-9_-]{1,128}$"),"invalid_packet_guid");
        var payload=(JObject)request.DeepClone(); payload.Remove("session_token"); var encoded=Canon(payload); Need(Encoding.UTF8.GetByteCount(encoded)<=131072,"stat_packet_too_large");
        var hash=Hash(encoded); Exec("INSERT OR IGNORE INTO stat_packets VALUES(?,?,?,?,?)",id,guid+":"+hash,hash,encoded,Now);
        return new JObject { ["ok"]=true,["modTime"]=Account(id)["revision"]!.DeepClone() };
    });
    public void LoadCatalog(string data)
    {
        Atomic(()=> {
            var before=Dump(new JArray(Query("SELECT * FROM catalog ORDER BY str_id")));
            foreach(var file in new[]{"catalog.json","extra-catalog.json"}) foreach(var item in (JArray)Parse(File.ReadAllText(Path.Combine(data,file)))["items"]!) {
                var name=item.S("str_id"); var price=item.I("price",-1); var currency=item.S("currency"); Need(price>=0 && price<=int.MaxValue && currency is "coins" or "diamonds","invalid_catalog_item");
                var existing=One("SELECT item_id FROM catalog WHERE str_id=?",name); var number=existing?.I("item_id") ?? item.I("item_id");
                if(existing==null && One("SELECT 1 FROM catalog WHERE item_id=?",number)!=null) number=One("SELECT MAX(item_id)+1 AS id FROM catalog")!.I("id");
                var config=item.S("config"); var value=config!="" ? JToken.Parse(File.ReadAllText(Path.Combine(data,config))):item["data"] ?? new JObject();
                Exec("INSERT INTO catalog VALUES(?,?,?,?,?,?,?) ON CONFLICT(str_id) DO UPDATE SET item_id=excluded.item_id,item_group=excluded.item_group,currency=excluded.currency,price=excluded.price,consumable=excluded.consumable,data_json=excluded.data_json",name,number,item.S("group"),currency,price,item.Bool("consumable")?1:0,Dump(value));
            }
            Exec("DELETE FROM catalog WHERE item_group='CarPaint' AND str_id LIKE 'CarPaint/%' AND NOT EXISTS (SELECT 1 FROM inventory WHERE inventory.str_id=catalog.str_id)");
            foreach(var m in (JArray)Parse(File.ReadAllText(Path.Combine(data,"missions.json")))["missions"]!) Exec("INSERT INTO mission_catalog VALUES(?,?,?,?,?,?,?) ON CONFLICT(name) DO UPDATE SET event_id=excluded.event_id,previous_name=excluded.previous_name,currency=excluded.currency,reward=excluded.reward,mode=excluded.mode,max_reward=excluded.max_reward",m.S("name"),m.I("id"),(string?)m["previous"],m.S("currency"),m.I("reward"),m.S("mode"),m.I("max_reward",5000));
            if(before!=Dump(new JArray(Query("SELECT * FROM catalog ORDER BY str_id")))) Exec("UPDATE accounts SET revision=revision+1"); return true;
        });
    }
    public void Dispose()=>connection.Dispose();
}
