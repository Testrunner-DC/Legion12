import {TerminalRecipientMarkers} from './terminal-recipient-markers.mjs'

// Diagnostic-only composition. It never selects an action, retries a command,
// changes the normal capacity guard, or claims that the diagnostic run passed it.
export function createTerminalRecipientTransportAdapter({nonce, originalMarkers,
  expectedCommands=30, now=()=>performance.now(), onMarker=()=>{}, onFault=()=>{}}) {
  for(const name of ['observeParsed','afterDispatch','recordLoopDrift','report'])
    if(typeof originalMarkers?.[name]!=='function') throw new Error('RECIPIENT_ORIGINAL_MARKERS_REQUIRED')
  const faults=new Set(), installed=new Map(), roomClients=new Set()
  const fault=code=>{faults.add(code);try{onFault(code)}catch{}}
  const recipient=new TerminalRecipientMarkers({nonce,expectedCommands,now,onMarker})
  const prefix=`l12diag:${nonce}:`
  function bindRoom(room) {
    const peers=[room?.host,room?.guest]
    if(peers.some(peer=>!peer?.socket||typeof peer.socket.send!=='function'||roomClients.has(peer))) {
      fault('RECIPIENT_NATIVE_BINDING_REJECTED');return false
    }
    if(!recipient.bindRoom(room)){fault('RECIPIENT_ROOM_BINDING_REJECTED');return false}
    for(const client of peers) {
      const socket=client.socket, prior=socket.send
      function send(raw,...args) {
        try {
          const text=typeof raw==='string'?raw:Buffer.isBuffer(raw)?raw.toString('utf8'):null
          if(text?.includes(prefix)) {
            const message=JSON.parse(text)
            if(typeof message.requestId==='string'&&message.requestId.startsWith(prefix)
              &&!recipient.observeNativeSend(client,message.requestId,now()))
              fault('RECIPIENT_NATIVE_BOUNDARY_REJECTED')
          }
        } catch {fault('RECIPIENT_NATIVE_OBSERVATION_FAILED')}
        return Reflect.apply(prior,this,[raw,...args])
      }
      socket.send=send;installed.set(client,{socket,send,prior});roomClients.add(client)
    }
    return true
  }
  return {
    bindRoom,
    markers:{
      tryBegin(args) {
        if(args.stage==='cleanup'&&args.commandType==='surrender') {
          const entry=installed.get(args.client)
          if(!entry||args.client.socket!==entry.socket||entry.socket.send!==entry.send) {
            fault('RECIPIENT_NATIVE_BINDING_DRIFT');return null
          }
        }
        return recipient.tryBegin(args)
      },
      observeParsed(client,value) {
        return {original:originalMarkers.observeParsed(client,value),recipient:recipient.observeParsed(client,value)}
      },
      afterDispatch(token,at) {
        originalMarkers.afterDispatch(token?.original,at)
        if(token?.recipient&&!recipient.afterDispatch(token.recipient,at))fault('RECIPIENT_DISPATCH_OBSERVATION_FAILED')
      },
      recordLoopDrift(value){originalMarkers.recordLoopDrift(value);recipient.recordLoopDrift(value)},
      report(...args){return originalMarkers.report(...args)},
    },
    diagnosticReport(){
      const report=recipient.report()
      return {...report,adapter_fault_count:faults.size,adapter_faults:[...faults],
        trace_complete:Number(report.trace_complete===1&&faults.size===0),online_capacity_accepted:0}
    },
    retire() {
      let restored=0,drifted=0
      for(const {socket,send,prior} of installed.values()) {
        if(socket.send===send){socket.send=prior;restored++}else drifted++
      }
      installed.clear();roomClients.clear()
      return {restored,drifted,originalGameCommandsCancelled:0}
    },
  }
}
