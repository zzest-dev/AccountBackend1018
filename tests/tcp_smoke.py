import json,socket,struct,subprocess,tempfile,time,sqlite3,sys
from contextlib import closing
from pathlib import Path
root=Path(__file__).resolve().parents[1]; temporary=tempfile.TemporaryDirectory(prefix='accountbackend1018-test-'); out=Path(temporary.name)
with socket.socket() as probe:
 probe.bind(('127.0.0.1',0)); port=probe.getsockname()[1]
config=dict(listen_host='127.0.0.1',listen_port=port,database=str(out/'accounts.sqlite3'),catalog_directory=str(root/'data'),new_account=dict(coins=10000000,diamonds=10000))
configfile=out/'server.json';configfile.write_text(json.dumps(config)); dll=root/'src/AccountBackend/bin/Release/net8.0/AccountBackend.dll'
assert not (out/'accounts.sqlite3').exists(),'Use a fresh TCP test database'
def string(s):
 b=s.encode();return struct.pack('<i',len(b))+b
def readexact(s,n):
 b=b''
 while len(b)<n:
  part=s.recv(n-len(b))
  if not part:raise EOFError()
  b+=part
 return b
def frame(s):return readexact(s,struct.unpack('>i',readexact(s,4))[0])
def send(s,b):s.sendall(struct.pack('>i',len(b))+b)
def connect():
 s=socket.create_connection(('127.0.0.1',port),3);s.settimeout(3);send(s,struct.pack('<i',8)+string('')+string(''));assert frame(s)==bytes(4);return s
index=0
def call(s,route,body,idx=None):
 global index
 index+=1;idx=idx or index;send(s,struct.pack('<hI',8,idx)+string(route)+bytes([10])+string('json')+string(json.dumps(body)))
 b=frame(s);magic,got,code=struct.unpack_from('<hIh',b);assert (magic,got)==(15777,idx);at=8
 def get():
  nonlocal at
  size=struct.unpack_from('<i',b,at)[0];at+=4;v=b[at:at+size].decode();at+=size;return v
 msg=get();typ=get();val=get() if typ!='none' else None;assert at==len(b)
 return code,msg,json.loads(val) if typ=='json' else val
def launch():
 p=subprocess.Popen(['dotnet',str(dll),'--config',str(configfile)],stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT)
 for _ in range(100):
  if p.poll()!=None:raise Exception('Test server exited: '+str(p.returncode))
  try:return p,connect()
  except OSError:time.sleep(.1)
 p.terminate();p.wait(10);raise Exception('startup timeout')
def login(s,token=None):
 c=call(s,'/main/open_connect/',dict(hand_shake='11111111-1111-1111-1111-111111111111'))[2]['connect_id']
 if token is None: token=call(s,'/main/register/',dict(connect_id=c,reg_token='FKDJfjLSdhHfKJDS',vProtocol=2))[2]['token']
 l=call(s,'/main/login/',dict(connect_id=c,token=token));assert l[0]==0,l;return token,l[2]['session_token']
p=s=None
try:
 p,s=launch();token,session=login(s);auth=dict(session_token=session)
 profile=call(s,'/user/get_cloud_data/',auth)[2];assert profile['User']['coins']==10000000 and profile['User']['nik']=='user 1';assert profile['Items']['CarSto']['Car_Susp_Change']['coins']==1700
 assert call(s,'/user/get_cloud_data/',dict(session_token='fake'))[0]==401
 req=dict(**auth,version=1,items=[dict(strID='Golf')]);a=call(s,'/sell/buy_items/',req,100);assert a[0]==0;assert call(s,'/sell/buy_items/',req,100)==a
 req=dict(**auth,version=1,items=[dict(strID='Car_Menu_FillNitro')]);assert call(s,'/sell/buy_items/',req,101)[0]==0
 assert call(s,'/user/try_apply_new_nik_name/',dict(**auth,nikName='Driver Test'))[2]['assigned']
 assert call(s,'/user/try_apply_new_nik_name/',dict(**auth,nikName='user 99'))[2]['assigned']==False
 start=dict(**auth,mission_str_id='Get Drugs');assert call(s,'/Mis/start/',start,200)[2]=='ok_begin'
 with closing(sqlite3.connect(out/'accounts.sqlite3')) as db,db:db.execute("UPDATE mission_runs SET started_at=? WHERE status='active'",(time.time()-12,))
 finish=dict(**start,is_win=True,spend_time_sec=10.5);reward=call(s,'/Mis/complete/',finish,201);assert reward[2]['add_diamonds']==250,reward;assert call(s,'/Mis/complete/',finish,202)==reward
 state=call(s,'/user/get_cloud_data/',auth)[2];assert state['User']['diamonds']==10200 and state['Mis']['Get Drugs']['replays']==0
 assert call(s,'/match/list/',dict(**auth,user_id=1))[2]=={'items':[]};assert call(s,'/match/try_connect_to_game_server/',dict(**auth,user_id=1))[0]==503
 s.close();p.terminate();p.wait(10);p,s=launch();_,session=login(s,token);state=call(s,'/user/get_cloud_data/',dict(session_token=session))[2];assert state['User']['diamonds']==10200 and state['User']['nik']=='Driver Test' and 'Golf' in state['Cars']
 with closing(sqlite3.connect(out/'accounts.sqlite3')) as db,db:assert db.execute('PRAGMA integrity_check').fetchone()[0]=='ok'
 bad=socket.create_connection(('127.0.0.1',port),3);bad.settimeout(3);bad.sendall(struct.pack('>i',1048577));assert bad.recv(1)==b'';bad.close()
 print('TCP PASS: native protocol, account, full catalog, buy/retry, nitro, nickname, mission/retry, progress, disabled multiplayer, oversized frame, restart/persistence, SQLite integrity')
finally:
 if s:s.close()
 if p and p.poll() is None:p.terminate();p.wait(10)

 temporary.cleanup()
