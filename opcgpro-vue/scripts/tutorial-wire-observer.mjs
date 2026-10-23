import assert from 'node:assert/strict'
import {createTestrunProtocol,currentProtocolClientBinding} from './testrun-pressure-protocol.mjs'

// Passive QA observation only. Use the actual shipped decoders, never alter a
// browser frame, send recovery, advance teaching or invent a request receipt.
export async function createTutorialWireObserver(){
  const clientBinding=currentProtocolClientBinding()
  const {codec}=await createTestrunProtocol(clientBinding)
  const statistics={frames:0,propertyFrames:0,cardFrames:0,failures:0}
  return {clientBinding,statistics,decode(raw){
    try{
      const encoded=String(raw),wire=JSON.parse(encoded)
      let value=wire,logicalSource=encoded
      statistics.frames++
      if(value?.type==='gameStatePropertyFrameV1'){
        statistics.propertyFrames++
        value=codec.restoreGameStatePropertyFrame(value,encoded,source=>{logicalSource=source})
        assert.ok(value,'TUTORIAL_PROPERTY_OBSERVATION_REJECTED')
      }
      if(value?.type==='gameStateCardFrameV1'){
        statistics.cardFrames++
        value=codec.restoreGameStateCardFrame(value,logicalSource)
        assert.ok(value,'TUTORIAL_CARD_OBSERVATION_REJECTED')
      }
      assert.ok(value&&typeof value==='object'&&!Array.isArray(value),'TUTORIAL_MESSAGE_NOT_OBJECT')
      return {wireType:wire.type,value}
    }catch(error){statistics.failures++;throw error}
  }}
}
