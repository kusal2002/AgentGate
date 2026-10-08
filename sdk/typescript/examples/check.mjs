import { randomUUID } from 'node:crypto';
import { AgentGate, AgentGateError } from '@agentgate/sdk';

const abort=new AbortController();
const cancel=()=>abort.abort();process.once('SIGINT',cancel);
try {
  const gate=new AgentGate({apiKey:process.env.AGENTGATE_API_KEY,baseUrl:process.env.AGENTGATE_BASE_URL||'http://localhost:5000'});
  const mode=process.argv[2]||'evaluate';
  if(mode==='evaluate') {
    const result=await gate.evaluate({action:'refund',resource:{type:'customer',id:'SDK-CHECK'},parameters:{amount:750,currency:'USD'},idempotencyKey:process.env.AGENTGATE_IDEMPOTENCY_KEY||`sdk-check-${randomUUID()}`},{signal:abort.signal});
    console.log(JSON.stringify({actionId:result.actionId,decision:result.decision,status:result.status,approvalId:result.approvalId},null,2));
  } else if(mode==='approval'||mode==='wait') {
    const id=process.env.AGENTGATE_APPROVAL_ID;
    const result=mode==='wait'?await gate.waitForApproval(id,{signal:abort.signal}):await gate.getApproval(id,{signal:abort.signal});
    console.log(JSON.stringify({approvalId:result.id,actionId:result.actionId,status:result.status,actionStatus:result.actionStatus},null,2));
  } else {throw new Error('Use evaluate, approval, or wait.');}
} catch(error) {
  console.error(error instanceof AgentGateError?`${error.name}: ${error.message}`:'SDK check failed. Check configuration and command arguments.');process.exitCode=1;
} finally {process.removeListener('SIGINT',cancel);}
