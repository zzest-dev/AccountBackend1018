using Newtonsoft.Json.Linq;
using System.Text;
using System.Text.RegularExpressions;
using static AccountBackend.J;

namespace AccountBackend;

public sealed class Commerce(Store db)
{
    JObject Entry(string name)=>db.One("SELECT * FROM catalog WHERE str_id=?",name) ?? throw new RequestError("item_not_found:"+name);
    static void Add(Dictionary<string,long> counts,string key,long amount=1) => counts[key]=counts.GetValueOrDefault(key)+amount;
    static JArray Array(JToken? x,string error) { Need(x is JArray,error); return (JArray)x!; }
    static JObject Object(JToken? x,string error) { Need(x is JObject,error); return (JObject)x!; }
    public JObject Purchase(long id,string requestKey,JObject request) => db.Atomic(()=> {
        var kind=request.S("type"); Need(kind is "" or "HumanSkin" or "CarTuning","unsupported_transaction_type");
        var items=Array(request["items"],"invalid_items"); Need(items.Count>=(kind=="CarTuning"?0:1) && items.Count<=100,"invalid_items");
        var counts=new Dictionary<string,long>(StringComparer.Ordinal);
        foreach(var item in items) {
            Need(item is JObject,"invalid_item"); var name=item.S("strID"); var count=item.I("count",1);
            Need(name.Length is >0 and <=256 && (item["count"]==null || item["count"]!.Type==JTokenType.Integer) && count>=(kind=="CarTuning" && name=="Paint_Car_OneVinil"?0:1) && count<=1000,"invalid_item");
            Add(counts,name,count); Need(counts[name]<=1000,"invalid_count");
        }
        var plainCars=kind=="" && counts.Keys.All(n=>Entry(n).S("item_group")=="Cars");
        var fingerprintNode=plainCars?(JToken)new JArray(counts.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>new JObject { ["strID"]=x.Key,["count"]=x.Value })):
            new JObject { ["type"]=request["type"]?.DeepClone(),["data"]=request["jsonData"]?.DeepClone(),["counts"]=JObject.FromObject(counts),["item_data"]=new JArray(items.Select(x=>x["jsonData"]?.DeepClone())) };
        var encoded=Canon(fingerprintNode,true); Need(Encoding.UTF8.GetByteCount(encoded)<=262144,"transaction_too_large"); var hash=Hash(encoded);
        var previous=db.One("SELECT * FROM purchases WHERE account_id=? AND request_key=?",id,requestKey);
        if(previous!=null) { Need(previous.S("fingerprint")==hash,"request_key_conflict"); return Parse(previous.S("result_json")); }
        var account=db.Account(id); var owned=db.Query("SELECT str_id FROM inventory WHERE account_id=?",id).Select(x=>x.S("str_id")).ToHashSet();
        var catalog=counts.Keys.ToDictionary(n=>n,Entry); string? block=null,subject=null,key=null; JObject? data=null;
        if(plainCars) foreach(var pair in counts) Need(!owned.Contains(pair.Key) && pair.Value==1,"already_owned_or_invalid_count");
        else if(kind=="HumanSkin") {
            data=Object(request["jsonData"],"invalid_skin"); subject=data.S("sex"); Need(subject is "Male" or "FeMale","invalid_skin"); block="Skins"; key=data.S("GUID");
            foreach(var pair in catalog) Need(pair.Value.S("item_group")==subject && counts[pair.Key]==1,"skin_group_or_count_mismatch");
            foreach(var slot in data.Properties().Where(p=>p.Name is not "sex" and not "GUID")) {
                var value=Object(slot.Value,"invalid_skin_slot"); Need(Keys(value,"item") && value.Count==1 && value["item"]?.Type==JTokenType.String,"invalid_skin_slot");
                var name=value.S("item"); var entry=Entry(name); Need(entry.S("item_group")==subject,"skin_not_in_catalog"); Need(Parse(entry.S("data_json")).S("slot")==slot.Name,"skin_slot_mismatch");
                Need(entry.I("price")==0 || owned.Contains(name) || counts.ContainsKey(name),"skin_not_owned");
            }
        }
        else if(kind=="CarTuning") {
            data=Object(request["jsonData"],"invalid_tuning"); Need(data.I("v")==2,"invalid_tuning");
            Need(Keys(data,"v","GUID","carName","engine","colors","wheels","items","number","WD","horn","upgrades"),"unknown_tuning_fields");
            var colors=Array(data["colors"],"invalid_colors"); Need(colors.Count<=100,"invalid_colors");
            subject=data.S("carName"); key=data.S("GUID"); block="Tunings"; Need(owned.Contains(subject),"car_not_owned");
            var old=db.One("SELECT data_json FROM saved_profiles WHERE account_id=? AND block=? AND subject=?",id,block,subject);
            var baseline=Parse((old??Entry(subject)).S("data_json")); var needed=new Dictionary<string,long>();
            Need(data["engine"]?.Type==JTokenType.String,"invalid_engine");
            if(!Same(data["colors"],baseline["colors"])) Add(needed,"Paint_Car_Body");
            if(!Same(data["engine"],baseline["engine"])) Add(needed,data.S("engine"));
            if(!Same(data["WD"],baseline["WD"])) { Need(data.S("WD") is "AWD" or "FWD" or "BWD","invalid_transmission"); Add(needed,"Car_Change_AWD"); }
            if(!Same(data["horn"],baseline["horn"])) { Need(Regex.IsMatch(data.S("horn"),@"^Horn_\d+$"),"invalid_horn"); Add(needed,"Car_Change_Horn"); }
            var number=Object(data["number"],"invalid_number"); var oldNumber=baseline["number"] as JObject ?? new JObject();
            Need(Keys(number,"number","country","flag","view"),"invalid_number"); var view=number.I("view"); Need((number["view"]==null || number["view"]!.Type==JTokenType.Integer) && view is >=0 and <=3,"invalid_number_view");
            if(view!=oldNumber.I("view")) Add(needed,"Car_Number_"+new[]{"Front_Back","Front","Back","None"}[(int)view]);
            if(new[]{"number","country","flag"}.Any(k=>!Same(number[k],oldNumber[k]))) Add(needed,"Car_Change_Number");
            Need(number.Properties().Where(p=>p.Name!="view").All(p=>Nil(p.Value)||p.Value.Type==JTokenType.String && (p.Value.Value<string>()??"").Length<=64),"invalid_number_text");
            if(!Same(data["upgrades"],baseline["upgrades"])) {
                var upgrades=Array(data["upgrades"],"invalid_upgrades"); Need(upgrades.Count<=64,"invalid_upgrades");
                var before=(baseline["upgrades"] as JArray ?? new JArray()).ToDictionary(x=>x.S("key"),x=>x.S("use")); var seen=new HashSet<string>();
                foreach(var up in upgrades) { var u=Object(up,"invalid_upgrade"); var k=u.S("key"); var use=u.S("use"); Need(Keys(u,"key","use") && u.Count==2 && u["use"]?.Type==JTokenType.String && before.ContainsKey(k) && seen.Add(k),"invalid_upgrade"); if(use!=before[k]) { Need(use=="No","body_upgrade_not_in_catalog"); Add(needed,"Car_Upgrade_No"); } }
                Need(seen.SetEquals(before.Keys),"missing_upgrade_slot");
            }
            Need(Nil(data["items"]) || data["items"] is JArray,"invalid_vinyl_layers"); var layers=data["items"] as JArray ?? new JArray(); ValidateVinyls(layers,owned,counts);
            if(!Same(layers,baseline["items"] as JArray ?? new JArray())) Add(needed,"Paint_Car_OneVinil",layers.Count);
            var wheels=Array(data["wheels"],"invalid_wheels"); var oldWheels=Array(baseline["wheels"],"invalid_wheels"); Need(wheels.Count==oldWheels.Count,"invalid_wheels");
            for(int i=0;i<wheels.Count;i++) {
                var w=Object(wheels[i],"invalid_wheel"); var b=Object(oldWheels[i],"invalid_wheel"); Need(w.Properties().All(p=>b.ContainsKey(p.Name)) && Same(w["idx"],b["idx"]),"invalid_wheel");
                Need(w["model"]?.Type==JTokenType.String && w["tire"]?.Type==JTokenType.String && w["colors"] is JArray,"invalid_wheel_data");
                if(!Same(w["colors"],b["colors"])) Add(needed,"Car_Paint_Wheel");
                if(!Same(w["tire"],b["tire"])) Add(needed,"Car_"+w.S("tire"));
                if(!Same(w["model"],b["model"])) Add(needed,"Car_"+w.S("model"));
                if(new[]{"rad","width","susp_height","susp_stiff","susp_z_ange","susp_x_offset","susp_y_offset"}.Any(k=>!Same(w[k],b[k]))) Add(needed,"Car_Susp_Change");
                Need(w.Properties().Where(p=>p.Name is not "model" and not "tire" and not "colors").All(p=>Number(p.Value) && Math.Abs((double)p.Value)<=1000),"invalid_wheel_value");
                foreach(var c in (JArray)w["colors"]!) ValidateColor(c);
            }
            foreach(var pair in needed) Need(counts.GetValueOrDefault(pair.Key)>=pair.Value,"unpaid_tuning_change:"+pair.Key);
            foreach(var pair in catalog) { Need(new[]{"CarPaint","CarEngine","CarWheel","CarTire","CarSto"}.Contains(pair.Value.S("item_group")),"invalid_tuning_item"); var limit=pair.Key=="Paint_Car_OneVinil"?512:Parse(pair.Value.S("data_json")).Bool("vinyl")?1:4; Need(counts[pair.Key]<=limit,"invalid_tuning_count"); }
            foreach(var c in colors) ValidateColor(c);
        }
        else if(!plainCars) foreach(var item in items) {
            var name=item.S("strID");
            if(new[]{"Car_Delivery_Slow","Car_Delivery_Normal","Car_Delivery_Fast"}.Contains(name)) Need(item["jsonData"] is JObject && owned.Contains(item["jsonData"].S("carName")) && counts[name]==1,"invalid_delivery_car");
            else if(new[]{"Car_Menu_FillNitro","Car_Garage_FillNitro","Car_Garge_Fix"}.Contains(name)) Need(counts[name]==1 && db.One("SELECT 1 FROM inventory JOIN catalog USING(str_id) WHERE account_id=? AND item_group='Cars'",id)!=null,"invalid_car_service");
            else Need(catalog[name].S("item_group")=="UserWeapone","unsupported_plain_purchase");
        }
        if(block!=null) { Need(Regex.IsMatch(key??"",@"^[A-Za-z0-9_-]{1,128}$"),"invalid_profile_guid"); var existing=db.One("SELECT subject FROM saved_profiles WHERE account_id=? AND block=? AND item_key=?",id,block,key); Need(existing==null || existing.S("subject")==subject,"profile_guid_conflict"); }
        long coins=0,diamonds=0; var grants=new List<(string,long)>();
        foreach(var pair in catalog) {
            var entry=pair.Value; var consumable=entry.I("consumable")!=0;
            if(!plainCars && !consumable && owned.Contains(pair.Key)) continue;
            var cost=checked(entry.I("price")*counts[pair.Key]); if(entry.S("currency")=="coins") coins=checked(coins+cost); else diamonds=checked(diamonds+cost);
            if(plainCars || kind!="CarTuning" && entry.S("item_group")!="CarSto" || !consumable) grants.Add((pair.Key,counts[pair.Key]));
        }
        Need(coins<=account.I("coins") && diamonds<=account.I("diamonds"),"insufficient_balance"); var now=Now;
        db.Exec("INSERT INTO purchases(account_id,request_key,fingerprint,created_at) VALUES(?,?,?,?)",id,requestKey,hash,now); var tid=db.LastId;
        db.Exec("UPDATE accounts SET coins=coins-?,diamonds=diamonds-?,revision=revision+1 WHERE id=?",coins,diamonds,id);
        foreach(var (name,count) in grants) db.Exec("INSERT INTO inventory VALUES(?,?,?,?,?) ON CONFLICT(account_id,str_id) DO UPDATE SET quantity=quantity+excluded.quantity,transaction_id=excluded.transaction_id,time_buy=excluded.time_buy",id,name,count,tid,now);
        if(block!=null) { db.Exec("DELETE FROM saved_profiles WHERE account_id=? AND block=? AND subject=?",id,block,subject); db.Exec("INSERT INTO saved_profiles VALUES(?,?,?,?,?)",id,block,key,subject,Dump(data)); }
        var result=new JObject { ["transaction_id"]=tid,["coins"]=account.I("coins")-coins,["diamonds"]=account.I("diamonds")-diamonds };
        if(plainCars) result["items"]=fingerprintNode.DeepClone();
        db.Exec("UPDATE purchases SET result_json=? WHERE id=?",Dump(result),tid); return result;
    });
    static void ValidateColor(JToken c) {
        Need(c is JObject && c["name"]?.Type==JTokenType.String && Regex.IsMatch(c.S("color"),"^#[0-9a-fA-F]{8}$"),"invalid_color");
        foreach(var key in new[]{"metallic","gloss"}) Need(Number(c[key]) && (double)c[key]! is >=0 and <=1,"invalid_material");
    }
    void ValidateVinyls(JArray layers,HashSet<string> owned,Dictionary<string,long> counts) {
        Need(layers.Count<=512,"invalid_vinyl_layers");
        foreach(var value in layers) {
            var l=Object(value,"invalid_vinyl_layer"); Need(Keys(l,"sprite","color","pos","rot","back","size_y","acpect","depth","symmetry","hidden","text"),"invalid_vinyl_layer");
            var sprite=l.S("sprite"); var e=Entry(sprite); Need(Parse(e.S("data_json")).Bool("vinyl"),"vinyl_not_in_catalog"); Need(e.I("price")==0 || owned.Contains(sprite) || counts.GetValueOrDefault(sprite)==1,"vinyl_not_owned");
            foreach(var k in new[]{"color","back"}) Need(Nil(l[k]) || Regex.IsMatch(l.S(k),"^#?[0-9a-fA-F]{8}$"),"invalid_vinyl_color");
            foreach(var k in new[]{"pos","rot"}) { var text=l.S(k); var words=text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries); Need(text.Length<=100 && words.Length==3 && words.All(s=>double.TryParse(s,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var n) && double.IsFinite(n) && Math.Abs(n)<=10000),"invalid_vinyl_transform"); }
            foreach(var k in new[]{"size_y","acpect","depth"}) Need(Number(l[k]) && (double)l[k]! is >0 and <=1000,"invalid_vinyl_dimensions");
            foreach(var k in new[]{"symmetry","hidden"}) Need(!l.ContainsKey(k)||l[k]!.Type==JTokenType.Boolean,"invalid_vinyl_flag");
            Need(!l.ContainsKey("text")||l["text"]!.Type==JTokenType.String && l.S("text").Length<=1024,"invalid_vinyl_text");
        }
    }
}
