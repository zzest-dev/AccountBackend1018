using Newtonsoft.Json.Linq;
using static AccountBackend.J;

namespace AccountBackend;

public record Reply(short Code, string Message, JToken? Body = null);
public sealed record Session(long UserId, double Expires);

public sealed class Router(Store db, IGameServerDirectory? directory=null)
{
    readonly object gate=new();
    readonly string node=Guid.NewGuid().ToString();
    readonly Dictionary<string,double> pending=new();
    readonly Dictionary<string,JObject> registrations=new();
    readonly Dictionary<string,Session> sessions=new();
    readonly Commerce commerce=new(db);
    readonly Missions missions=new(db);
    readonly IGameServerDirectory gameServers=directory??new DisabledGameServerDirectory();
    public Reply Dispatch(string route,string type,string body,uint index) {
        lock(gate) {
            try { Need(type=="json","JSON request expected"); return Handle(route,JObject.Parse(body),index); }
            catch(Newtonsoft.Json.JsonException) { return new(400,"Invalid JSON"); }
            catch(RequestError e) { return new(route.StartsWith("/match/")? (short)503:(short)400,e.Message); }
            catch(OverflowException) { return new(400,"balance_overflow"); }
        }
    }
    bool Connection(string id)=>pending.TryGetValue(id,out var stamp) && Time-stamp<300;
    Reply Handle(string route,JObject request,uint index) {
        Reply Ok(JToken? value=null)=>new(0,"",value);
        if(route=="/mirror/ping/") return Ok(new JObject { ["ok"]=true,["users"]=0 });
        if(route is "/main/hand_shake/" or "/mirror/hand_shake/") return Ok(new JObject { ["guid"]=node });
        if(route=="/main/open_connect/") {
            if(!Guid.TryParse(request.S("hand_shake"),out var guid) || guid==Guid.Empty) return new(401,"Handshake required");
            foreach(var key in pending.Keys.Where(k=>Time-pending[k]>=300).ToArray()) { pending.Remove(key); registrations.Remove(key); }
            if(pending.Count>=1024) return new(429,"Too many pending connections"); var token=Random(24); pending[token]=Time;
            return Ok(new JObject { ["connect_id"]=token,["technicalWorksOnServer"]=false });
        }
        if(route=="/main/register/") {
            var id=request.S("connect_id"); if(!Connection(id)) return new(401,"connect_id_fail");
            Need(request.S("reg_token")=="FKDJfjLSdhHfKJDS" && request.I("vProtocol")==2,"Invalid registration protocol");
            if(!Nil(request["login_with_id"]) && request["login_with_id"]!.ToString()!="" && request["login_with_id"]!.ToString()!="False") return new(501,"External provider login is not available on this private backend");
            if(!registrations.ContainsKey(id)) registrations[id]=db.Register(); return Ok(registrations[id].DeepClone());
        }
        if(route=="/main/login/") {
            if(!Connection(request.S("connect_id"))) return new(401,"connect_id_fail"); var account=db.Authenticate(request.S("token")); if(account==null) return new(401,"not found access token");
            foreach(var old in sessions.Keys.Where(k=>sessions[k].Expires<=Time).ToArray()) sessions.Remove(old);
            var token=Random(); sessions[token]=new(account.I("id"),Time+86400);
            return Ok(new JObject { ["ID"]=account.I("id"),["session_token"]=token,["session_id"]=System.Security.Cryptography.RandomNumberGenerator.GetInt32(1,int.MaxValue) });
        }
        var routes=new[]{"/main/check_state/","/user/get_cloud_data/","/sell/buy_items/","/stat/data/","/user/check_nik_name/","/user/try_apply_new_nik_name/","/Mis/start/","/Mis/complete/","/match/list/","/match/try_connect_to_game_server/"};
        if(!routes.Contains(route)) return new(503,"Private backend: route is not implemented yet");
        var sessionToken=request.S("session_token",route.StartsWith("/match/")?request.S("token"):"");
        if(!sessions.TryGetValue(sessionToken,out var session) || session.Expires<=Time) return new(401,"token_fail"); var user=session.UserId;
        var requestKey=Hash(sessionToken+"\n"+index);
        switch(route) {
            case "/main/check_state/": return Ok(new JObject());
            case "/user/check_nik_name/": return Ok(db.Nickname(user,request["nikName"],false));
            case "/user/try_apply_new_nik_name/": return Ok(db.Nickname(user,request["nikName"],true));
            case "/stat/data/": return Ok(db.Stats(user,request));
            case "/sell/buy_items/": Need(request.I("version")==1,"Unsupported transaction type"); return Ok(commerce.Purchase(user,requestKey,request));
            case "/Mis/start/": missions.Process(user,route,requestKey,request); return Ok(new JValue("ok_begin"));
            case "/Mis/complete/": return Ok(missions.Process(user,route,requestKey,request));
            case "/match/list/": case "/match/try_connect_to_game_server/":
                if(request["user_id"]?.Type!=JTokenType.Integer || request.I("user_id")!=user) return new(401,"user_id_fail");
                return Ok(route=="/match/list/"?gameServers.List(user,request):gameServers.Connect(user,request));
            default: return Ok(Profile(user));
        }
    }
    JObject Profile(long id) {
        var account=db.Account(id); var profile=new JObject(); foreach(var name in new[]{"Cars","Tunings","Skins","Weapons","Items","Owns","Mis","RemSt","Ads","InApp"}) profile[name]=new JObject();
        profile["modTime"]=account.I("revision"); profile["Mis"]=missions.Progress(id);
        profile["User"]=new JObject { ["user_id"]=id,["nik"]=account.S("nickname"),["ab"]=0,["coins"]=account.I("coins"),["diamonds"]=account.I("diamonds"),["fb_id"]="",["play_time"]=0.0,["icon_date"]=0 };
        foreach(var item in db.Query("SELECT * FROM catalog ORDER BY item_id")) {
            var group=item.S("item_group"); profile["Items"]![group] ??=new JObject(); var value=new JObject { ["id"]=item.I("item_id"),[item.S("currency")=="coins"?"coins":"diams"]=item.I("price") };
            if(group=="UserWeapone") value["json"]=item.S("data_json"); profile["Items"]![group]![item.S("str_id")]=value;
        }
        foreach(var item in db.Query("SELECT inventory.*,catalog.item_id,catalog.item_group,catalog.data_json FROM inventory JOIN catalog USING(str_id) WHERE account_id=?",id)) {
            var group=item.S("item_group"); var value=new JObject { ["item_id"]=item.I("item_id"),["time_buy"]=item.I("time_buy"),["transaction_id"]=item.I("transaction_id") };
            if(group=="UserWeapone") { value["plus"]=Math.Min(65535,item.I("quantity")*Parse(item.S("data_json")).I("buy_bullets")); value["minus"]=0; }
            profile[group=="Cars"?"Cars":group=="UserWeapone"?"Weapons":"Owns"]![item.S("str_id")]=value;
        }
        foreach(var saved in db.Query("SELECT * FROM saved_profiles WHERE account_id=?",id)) profile[saved.S("block")]![saved.S("item_key")]=JToken.Parse(saved.S("data_json")); return profile;
    }
}
