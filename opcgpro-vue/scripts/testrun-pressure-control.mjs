import assert from 'node:assert/strict'
import fs from 'node:fs'
import {spawn} from 'node:child_process'
import crypto from 'node:crypto'
import {strictSshArguments,digest} from './testrun-pressure-protocol.mjs'

// One pinned, bounded, stdin-only SSH control channel. No listening service,
// remote installation, credential log, command retry or service mutation here.
export const CONTROL_BOOTSTRAP=String.raw`
import base64,contextlib,hashlib,io,json,os,pathlib,signal,socket,sys
sys.dont_write_bytecode=True
def emit(value):
 raw=json.dumps(value,separators=(',',':')).encode();assert len(raw)<=262144
 sys.stdout.buffer.write(raw+b'\n');sys.stdout.buffer.flush()
pid=os.getpid();ticks=pathlib.Path('/proc/self/stat').read_text().rsplit(')',1)[1].split()[19]
identity={'schema':1,'kind':'normal-control-ready','pid':pid,'startTicks':ticks,'bootId':pathlib.Path('/proc/sys/kernel/random/boot_id').read_text().strip(),'hostname':socket.gethostname(),'uid':os.geteuid()}
emit(identity)
def expired(*unused):raise TimeoutError('CONTROL_REQUEST_DEADLINE')
signal.signal(signal.SIGALRM,expired)
while True:
 raw=sys.stdin.buffer.readline(614401)
 if not raw:break
 assert len(raw)<=614400 and raw.endswith(b'\n')
 request=json.loads(raw);request_id=request.get('id');assert isinstance(request_id,str) and len(request_id)==32
 if request.get('kind')=='close':
  emit({'schema':1,'kind':'normal-control-closed','id':request_id,'pid':pid,'startTicks':ticks,'bootId':identity['bootId']});break
 assert request.get('kind')=='execute' and type(request.get('timeoutSeconds')) is int and 1<=request['timeoutSeconds']<=45
 source=base64.b64decode(request['sourceBase64'],validate=True);assert len(source)<18000
 output=io.StringIO();errors=io.StringIO();signal.alarm(request['timeoutSeconds'])
 try:
  with contextlib.redirect_stdout(output),contextlib.redirect_stderr(errors):exec(compile(source,'<owned-normal-control>','exec'),{'p':request['parameters']})
  signal.alarm(0);text=output.getvalue();assert len(text.encode())<=262144 and not errors.getvalue()
  result=json.loads(text);emit({'schema':1,'id':request_id,'ok':True,'result':result})
 except Exception as error:
  signal.alarm(0);emit({'schema':1,'id':request_id,'ok':False,'errorClass':type(error).__name__})
 finally:output.close();errors.close()
`
const SSH='C:/Windows/System32/OpenSSH/ssh.exe'
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms))
async function within(promise,milliseconds){let timer;try{return await Promise.race([promise,new Promise(resolve=>{timer=setTimeout(()=>resolve(null),milliseconds)})])}finally{clearTimeout(timer)}}
const posix=value=>"'"+String(value).replaceAll("'","'\\''")+"'"
export function openOwnedControl(options={}){
 return createOwnedControl(options,940)
}
export function openTerminalDiagnosticControl({diagnosticSetting,...options}={}){
 assert.match(diagnosticSetting??'',/^terminal-recipients-v4:30:[a-f0-9]{32}$/,'EXACT_TERMINAL_RECIPIENT_PROFILE_REQUIRED')
 return createOwnedControl(options,1400)
}
function createOwnedControl({binding,processFactory=spawn,timeoutMs=12000},seconds){
 for(const name of ['test','formal'])assert.ok(Number.isSafeInteger(binding?.[name]?.pid)&&binding[name].pid>0)
 assert.ok(timeoutMs>=100&&timeoutMs<=15000)
 const bootstrap=Buffer.from(CONTROL_BOOTSTRAP).toString('base64')
 const command='timeout '+seconds+' python3 -I -u -B -c '+posix('import base64;exec(compile(base64.b64decode('+JSON.stringify(bootstrap)+'),"<normal-control-wire>","exec"))')
 const child=processFactory(SSH,[...strictSshArguments(),'-o','ServerAliveInterval=5','-o','ServerAliveCountMax=2',command],{windowsHide:true,stdio:['pipe','pipe','pipe']})
 let buffer='',bytes=0,errorBytes=0,ready=null,fault=null,pending=null,closed=null,queue=Promise.resolve()
 const fail=code=>{fault??=code;if(pending){pending.reject(new Error(code));pending=null}}
 const exited=new Promise(resolve=>{child.once('error',()=>{fail('CONTROL_SSH_START_FAILED');resolve({code:null})});child.once('close',(code,signal)=>{if(!closed)fail('CONTROL_SSH_CLOSED_EARLY');resolve({code,signal})})})
 child.stdin.on('error',()=>fail('CONTROL_INPUT_CLOSED'))
 child.stderr.on('data',chunk=>{errorBytes+=chunk.length;fail('CONTROL_STDERR_NOT_EMPTY')})
 child.stdout.on('data',chunk=>{
  bytes+=chunk.length;if(bytes>16*1024**2){fail('CONTROL_TOTAL_OUTPUT_BOUND');child.kill();return}
  buffer+=chunk.toString('utf8')
  while(buffer.includes('\n')){
   const at=buffer.indexOf('\n'),line=buffer.slice(0,at);buffer=buffer.slice(at+1)
   try{
    assert.ok(Buffer.byteLength(line)<=262144);const value=JSON.parse(line);assert.equal(value.schema,1)
    if(value.kind==='normal-control-ready'){
     assert.equal(ready,null);assert.equal(value.hostname,'ser550468033481');assert.equal(value.uid,0)
     assert.ok(Number.isSafeInteger(value.pid)&&value.pid>1&&![binding.test.pid,binding.formal.pid].includes(value.pid))
     assert.match(String(value.startTicks),/^[0-9]+$/);assert.match(value.bootId,/^[a-f0-9-]{36}$/);ready=value
    }else{
     assert.ok(ready&&pending&&value.id===pending.id)
     const selected=pending
     if(value.kind==='normal-control-closed'){
      for(const field of ['pid','startTicks','bootId'])assert.equal(value[field],ready[field]);pending=null;closed=value;selected.resolve(value)
     }else{assert.equal(typeof value.ok,'boolean');pending=null;if(value.ok)selected.resolve(value.result);else selected.reject(new Error('CONTROL_REMOTE_EXECUTION_REJECTED'))}
    }
   }catch{fail('CONTROL_REPLY_INVALID')}
  }
  if(Buffer.byteLength(buffer)>262144){fail('CONTROL_PARTIAL_OUTPUT_BOUND');child.kill()}
 })
 const waitReady=async()=>{const end=Date.now()+timeoutMs;while(Date.now()<end){if(fault)throw new Error(fault);if(ready)return ready;await delay(10)}throw new Error('CONTROL_READY_DEADLINE')}
 const exchange=async(request,milliseconds)=>{
  const previous=queue;let release;queue=new Promise(resolve=>release=resolve);await previous
  let timer
  try{
   assert.ok(ready&&!fault&&!closed&&pending===null,'CONTROL_CHANNEL_NOT_AVAILABLE')
   const id=crypto.randomUUID().replaceAll('-',''),frame=Buffer.from(JSON.stringify({...request,id})+'\n');assert.ok(frame.length<=614400)
   const reply=new Promise((resolve,reject)=>{pending={id,resolve,reject};timer=setTimeout(()=>fail('CONTROL_REQUEST_DEADLINE'),milliseconds)})
   child.stdin.write(frame,()=>frame.fill(0));return await reply
  }finally{clearTimeout(timer);release()}
 }
 const close=async()=>{
  let result=null
  try{if(ready&&!fault&&!closed)result=await exchange({kind:'close'},10000);if(result)child.stdin.end()
   if(fault||!ready)child.stdin.end()
   let exit=await within(exited,10000)
   if(!exit){child.kill();exit=await within(exited,2000)}
   assert.ok(result&&exit?.code===0&&errorBytes===0&&buffer===''&&!fault,'CONTROL_EXIT_UNPROVEN')
   return {schema:1,ready,closed:result,transportExitCode:exit.code,ownControlExited:true,sshConnections:1,credentialsEmitted:false,bootstrapSha256:digest(CONTROL_BOOTSTRAP)}
  }finally{
   if(child.exitCode===null&&child.signalCode===null)child.kill()
   const ended=await within(exited,2000)
   assert.ok(ended,'CONTROL_OWNED_TRANSPORT_CLOSE_UNPROVEN')
   child.stdout.removeAllListeners('data');child.stderr.removeAllListeners('data')
  }
 }
 return {waitReady,close,state:()=>({ready,fault,closed:Boolean(closed)}),
  async call(source,parameters,{timeoutMs=50000}={}){
   assert.ok(typeof source==='string'&&Buffer.byteLength(source)<18000&&Buffer.byteLength(JSON.stringify(parameters))<524288)
   assert.ok(Number.isFinite(timeoutMs)&&timeoutMs>=1000&&timeoutMs<=60000)
   return exchange({kind:'execute',sourceBase64:Buffer.from(source).toString('base64'),parameters,timeoutSeconds:Math.max(1,Math.min(45,Math.floor(timeoutMs/1000)-1))},timeoutMs)
  }}
}
