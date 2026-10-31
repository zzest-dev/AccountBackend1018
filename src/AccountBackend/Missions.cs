using Newtonsoft.Json.Linq;
using System.Text;
using static AccountBackend.J;

namespace AccountBackend;

public sealed class Missions(Store db)
{
    public JToken Process(long id,string route,string key,JObject request) => db.Atomic<JToken>(()=> {
        var payload=(JObject)request.DeepClone(); payload.Remove("session_token"); Need(!payload.Descendants().Any(x=>x.Type==JTokenType.Float && !Number(x)),"invalid_mission_data"); var encoded=Canon(payload); Need(Encoding.UTF8.GetByteCount(encoded)<=32768,"mission_data_too_large"); var digest=Hash(encoded); var now=Time;
        var previous=db.One("SELECT * FROM mission_requests WHERE account_id=? AND route=? AND request_key=?",id,route,key);
        if(previous!=null) { Need(previous.S("fingerprint")==digest,"mission_request_conflict"); return JToken.Parse(previous.S("response_json")); }
        var name=request.S("mission_str_id"); Need(name.Length is >0 and <=128,"invalid_mission_name"); var mission=db.One("SELECT * FROM mission_catalog WHERE name=?",name);
        if(mission==null) { var matches=db.Query("SELECT * FROM mission_catalog WHERE TRIM(name)=?",name.Trim()); Need(matches.Count==1,"mission_not_found"); mission=matches[0]; }
        name=mission.S("name"); JObject result;
        var active=db.One("SELECT * FROM mission_runs WHERE account_id=? AND status='active' ORDER BY id DESC LIMIT 1",id);
        if(route=="/Mis/start/") {
            var before=mission.S("previous_name"); Need(before=="" || db.One("SELECT 1 FROM mission_progress WHERE account_id=? AND mission_name=?",id,before)!=null,"previous_mission_not_completed");
            string token;
            if(active!=null && active.S("mission_name")==name && now-active.D("started_at")<=30) token=active.S("run_token");
            else { db.Exec("UPDATE mission_runs SET status='abandoned',finished_at=? WHERE account_id=? AND status='active'",now,id); token=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(); db.Exec("INSERT INTO mission_runs(account_id,mission_name,run_token,started_at,status) VALUES(?,?,?,?,'active')",id,name,token,now); }
            result=new JObject { ["ok_begin"]=true,["run_id"]=token,["reward_currency"]=mission.S("currency"),["reward"]=mission.I("reward") };
        } else {
            Need(request["is_win"]?.Type==JTokenType.Boolean && Number(request["spend_time_sec"]) && request.D("spend_time_sec") is >=0 and <=21600,"invalid_mission_result");
            Need(Nil(request["type"]) || request["type"]!.Type==JTokenType.String && request.S("type").Length<=64,"invalid_mission_type");
            Need(Nil(request["completeData"]) || request["completeData"] is JObject,"invalid_complete_data"); var details=request["completeData"] as JObject ?? new JObject(); var win=request.Bool("is_win"); var seconds=request.D("spend_time_sec");
            var old=db.One("SELECT receipt_json FROM mission_runs WHERE account_id=? AND mission_name=? AND finish_fingerprint=? AND receipt_json IS NOT NULL ORDER BY id DESC LIMIT 1",id,name,digest);
            if(old!=null && (active==null || active.S("mission_name")!=name)) result=Parse(old.S("receipt_json"));
            else {
                Need(active!=null && active.S("mission_name")==name,"mission_not_started");
                Need(!request.ContainsKey("run_id") || request.S("run_id")==active!.S("run_token"),"mission_run_mismatch"); var elapsed=now-active!.D("started_at");
                Need(elapsed is >=0 and <=21600 && seconds<=elapsed+30,"invalid_mission_duration"); Need(!win || seconds>=0.1 && elapsed>=1,"mission_completed_too_early"); long reward=0;
                if(win) {
                    reward=mission.I("reward"); if(mission.S("mode")=="time") { Need(details["timeElapse"]?.Type==JTokenType.Integer && details.I("timeElapse") is >=1 and <=5,"invalid_race_time_result"); reward=Math.Min(details.I("timeElapse")*1000,mission.I("max_reward")); }
                    if(mission.S("mode")=="race") Need(details["pos"]?.Type==JTokenType.Integer && details.I("pos") is >=0 and <=128,"invalid_race_position");
                    var currency=mission.S("currency"); Need(currency is "coins" or "diamonds","invalid_mission_currency"); Need(db.Account(id).I(currency)+reward<=int.MaxValue,"balance_overflow");
                    db.Exec($"UPDATE accounts SET {currency}={currency}+?,revision=revision+1 WHERE id=?",reward,id);
                    db.Exec("INSERT INTO mission_progress VALUES(?,?,?,?,?,?,?) ON CONFLICT(account_id,mission_name) DO UPDATE SET wins=wins+1,last_win=excluded.last_win,best_seconds=MIN(best_seconds,excluded.best_seconds),last_run=excluded.last_run",id,name,1,(long)now,(long)now,seconds,active.I("id"));
                }
                result=new JObject { ["result"]=true,["add_coins"]=mission.S("currency")=="coins"?reward:0,["add_diamonds"]=mission.S("currency")=="diamonds"?reward:0,["is_win"]=win };
                db.Exec("UPDATE mission_runs SET status=?,finished_at=?,finish_fingerprint=?,receipt_json=? WHERE id=?",win?"won":"lost",now,digest,Dump(result),active.I("id"));
            }
        }
        db.Exec("INSERT INTO mission_requests VALUES(?,?,?,?,?)",id,route,key,digest,Dump(result)); return result;
    });
    public JObject Progress(long id) {
        var result=new JObject(); foreach(var row in db.Query("SELECT p.*,c.event_id FROM mission_progress p JOIN mission_catalog c ON c.name=p.mission_name WHERE account_id=?",id)) {
            var value=new JObject { ["id"]=row.I("event_id"),["replays"]=row.I("wins")-1,["time_win"]=row.I("first_win"),["db_event_id"]=row.I("last_run") }; result[row.S("mission_name")]=value; result.TryAdd(row.S("mission_name").Trim(),value.DeepClone());
        } return result;
    }
}
