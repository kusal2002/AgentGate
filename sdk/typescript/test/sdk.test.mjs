import { test } from 'node:test';
import assert from 'node:assert/strict';
import { inspect } from 'node:util';
import { createRequire } from 'node:module';
import { createServer } from 'node:http';
import { AgentGate, AgentGateAbortError, AgentGateHttpError, AgentGateNetworkError, AgentGateProtocolError, AgentGateTimeoutError, AgentGateValidationError } from '@agentgate/sdk';

const key = 'synthetic-agent-key-not-a-real-credential';
const actionId = '11111111-1111-4111-8111-111111111111';
const approvalId = '22222222-2222-4222-8222-222222222222';
const date = '2026-10-08T12:00:00.0000000+00:00';
const request = () => ({action:'refund',resource:{type:'customer',id:'CUS-102'},parameters:{amount:750,currency:'USD'},idempotencyKey:'refund-8821'});
function evaluation(decision='review') {
  return {actionId,decision,status:decision==='review'?'awaiting_approval':decision==='allow'?'approved':'denied',reason:'Policy result',testEvaluation:false,
    matchedPolicyId:null,matchedPolicyName:null,reviewerRole:decision==='review'?'Reviewer':null,riskLevel:'Medium',policyUpdatedAt:null,
    approvalId:decision==='review'?approvalId:null,approvalStatus:decision==='review'?'pending':null,approvalExpiresAt:decision==='review'?date:null};
}
function approval(status='pending') {return {id:approvalId,actionId,status,actionStatus:status==='pending'?'awaiting_approval':status==='expired'||status==='cancelled'?'cancelled':status,
  reviewerRole:'Reviewer',requestedAt:date,expiresAt:date,resolvedAt:status==='pending'?null:date};}
const response = value => new Response(JSON.stringify(value),{headers:{'content-type':'application/json'}});
const client = (fetch,extra={}) => new AgentGate({apiKey:key,baseUrl:'http://localhost:5000',fetch,retryDelayMs:1,...extra});

for(const decision of ['allow','review','deny']) test(`evaluate returns the typed ${decision} response and safe request settings`,async()=>{
  let seen;
  const gate=client(async(url,init)=>{seen={url,init};return response(evaluation(decision));});
  const result=await gate.evaluate(request());assert.equal(result.decision,decision);assert.equal(seen.url.toString(),'http://localhost:5000/v1/actions/evaluate');
  assert.equal(seen.init.headers.get('authorization'),'Bearer '+key);assert.equal(seen.init.method,'POST');assert.equal(seen.init.redirect,'error');assert.equal(seen.init.credentials,'omit');assert.equal(seen.init.cache,'no-store');
  assert.deepEqual(JSON.parse(seen.init.body),request());assert.equal(seen.init.headers.get('content-type'),'application/json');
});
test('base paths and trailing slashes preserve reverse-proxy prefixes',async()=>{
  for(const baseUrl of ['https://gateway.example.test/agentgate','https://gateway.example.test/agentgate/']){
    const gate=client(async url=>{assert.equal(url.toString(),'https://gateway.example.test/agentgate/v1/approvals/'+approvalId);return response(approval());},{baseUrl});
    assert.equal((await gate.getApproval(approvalId.toUpperCase())).id,approvalId);
  }
});
test('retries preserve the exact serialized body despite caller mutation',async()=>{
  const bodies=[];const input=request();let calls=0;
  const gate=client(async(_url,init)=>{bodies.push(init.body);calls++;if(calls===1){input.parameters.amount=999;input.idempotencyKey='changed-by-caller';return new Response(key,{status:503});}return response(evaluation());});
  await gate.evaluate(input);assert.equal(calls,2);assert.equal(bodies[0],bodies[1]);assert.equal(JSON.parse(bodies[1]).idempotencyKey,'refund-8821');assert.equal(JSON.parse(bodies[1]).parameters.amount,750);
});
for(const status of [400,401,403,404,409,413,422]) test(`HTTP ${status} fails immediately without exposing response contents`,async()=>{
  let calls=0;const gate=client(async()=>{calls++;return new Response(key,{status});});
  await assert.rejects(gate.evaluate(request()),error=>error instanceof AgentGateHttpError&&error.statusCode===status&&!inspect(error,{showHidden:true}).includes(key));assert.equal(calls,1);
});
for(const status of [408,429,500,502,503,504]) test(`HTTP ${status} is retried with a bounded attempt count`,async()=>{
  let calls=0;const gate=client(async()=>{calls++;return calls<3?new Response('',{status,headers:{'retry-after':'0'}}):response(evaluation());});
  await gate.evaluate(request());assert.equal(calls,3);
});
test('retry exhaustion returns the last HTTP error',async()=>{
  let calls=0;const gate=client(async()=>{calls++;return new Response(key,{status:503});},{maxRetries:1});
  await assert.rejects(gate.evaluate(request()),error=>error instanceof AgentGateHttpError&&error.statusCode===503);assert.equal(calls,2);
});
test('retry-after larger than the configured wait cap returns without retrying early',async()=>{
  let calls=0;const gate=client(async()=>{calls++;return new Response('',{status:429,headers:{'retry-after':'120'}});},{maxRetryDelayMs:10});
  await assert.rejects(gate.evaluate(request()),error=>error instanceof AgentGateHttpError&&error.retryAfterMs===120_000);assert.equal(calls,1);
});
test('HTTP-date retry-after is exposed safely',async()=>{
  const gate=client(async()=>new Response('',{status:429,headers:{'retry-after':new Date(Date.now()+120_000).toUTCString()}}),{maxRetries:0});
  await assert.rejects(gate.evaluate(request()),error=>error instanceof AgentGateHttpError&&error.retryAfterMs>110_000);
});
test('transport failures retry but do not retain a secret-bearing cause',async()=>{
  let calls=0;const gate=client(async()=>{calls++;throw new Error('request headers: '+key);},{maxRetries:1});
  await assert.rejects(gate.evaluate(request()),error=>error instanceof AgentGateNetworkError&&!inspect(error,{showHidden:true}).includes(key)&&error.cause===undefined);assert.equal(calls,2);
});
for(const status of ['approved','rejected','expired','cancelled']) test(`wait returns authoritative ${status} status`,async()=>{
  let calls=0;const gate=client(async()=>response(approval(++calls===1?'pending':status)));
  const result=await gate.waitForApproval(approvalId,{pollIntervalMs:100,timeoutMs:2000});assert.equal(result.status,status);assert.equal(calls,2);
});
test('wait polls a pending result even if its date is in the client past',async()=>{
  let calls=0;const gate=client(async()=>response(approval(++calls===1?'pending':'approved')));
  assert.equal((await gate.waitForApproval(approvalId,{pollIntervalMs:100,timeoutMs:2000})).status,'approved');assert.equal(calls,2);
});
test('wait returns an already resolved request without sleeping',async()=>{
  let calls=0;const gate=client(async()=>{calls++;return response(approval('approved'));});
  assert.equal((await gate.waitForApproval(approvalId)).status,'approved');assert.equal(calls,1);
});
test('whole-operation timeout aborts retries and cannot become a deny result',async()=>{
  const gate=client(async()=>new Response('',{status:503}),{retryDelayMs:1000});
  await assert.rejects(gate.evaluate(request(),{timeoutMs:20}),AgentGateTimeoutError);
});
test('a hung custom fetch is bounded by the request timeout',async()=>{
  const gate=client(()=>new Promise(()=>{}),{requestTimeoutMs:20});await assert.rejects(gate.getApproval(approvalId),AgentGateTimeoutError);
});
test('wait timeout remains a wait timeout during an in-flight poll',async()=>{
  const gate=client(()=>new Promise(()=>{}));
  await assert.rejects(gate.waitForApproval(approvalId,{timeoutMs:20}),error=>error instanceof AgentGateTimeoutError&&error.operation==='waitForApproval');
});
test('cancellation before a call sends no request and hides abort reasons',async()=>{
  let calls=0;const controller=new AbortController();controller.abort(key);const gate=client(async()=>{calls++;return response(evaluation());});
  await assert.rejects(gate.evaluate(request(),{signal:controller.signal}),error=>error instanceof AgentGateAbortError&&!inspect(error).includes(key));assert.equal(calls,0);
});
test('cancellation interrupts polling sleep',async()=>{
  let calls=0;const controller=new AbortController();const gate=client(async()=>{calls++;setTimeout(()=>controller.abort(key),10);return response(approval());});
  await assert.rejects(gate.waitForApproval(approvalId,{signal:controller.signal,pollIntervalMs:2000}),AgentGateAbortError);assert.equal(calls,1);
});
test('cancellation interrupts retry backoff',async()=>{
  const controller=new AbortController();const gate=client(async()=>{setTimeout(()=>controller.abort(key),10);return new Response('',{status:503});},{retryDelayMs:1000});
  await assert.rejects(gate.evaluate(request(),{signal:controller.signal}),AgentGateAbortError);
});
test('response body reading also respects the operation deadline',async()=>{
  const gate=client(async()=>new Response(new ReadableStream({start(){}}),{headers:{'content-type':'application/json'}}));
  await assert.rejects(gate.evaluate(request(),{timeoutMs:20}),AgentGateTimeoutError);
});
for(const malformed of [null,{}, {...evaluation(),decision:['allow']},{...evaluation('allow'),testEvaluation:true},{...evaluation(),approvalId:null},{...evaluation(),status:'approved'}, {...evaluation(),riskLevel:['Medium']}])
  test('malformed evaluation responses fail closed',async()=>{const gate=client(async()=>response(malformed));await assert.rejects(gate.evaluate(request()),AgentGateProtocolError);});
test('malformed, non-JSON, oversized, and invalid UTF-8 bodies fail closed',async()=>{
  const values=[()=>new Response('{',{headers:{'content-type':'application/json'}}),()=>new Response('<html>sign in</html>'),
    ()=>new Response(' '.repeat(65_537),{headers:{'content-type':'application/json'}}),()=>new Response(new Uint8Array([255]),{headers:{'content-type':'application/json'}})];
  for(const make of values) await assert.rejects(client(async()=>make()).evaluate(request()),AgentGateProtocolError);
});
for(const malformed of [{...approval('approved'),actionStatus:'denied'},{...approval(),id:actionId},{...approval('approved'),resolvedAt:null},{...approval(),status:['pending']}])
  test('uncorrelated or inconsistent approvals fail closed',async()=>{await assert.rejects(client(async()=>response(malformed)).getApproval(approvalId),AgentGateProtocolError);});
test('invalid IDs and polling configuration fail before transport',async()=>{
  let calls=0;const gate=client(async()=>{calls++;return response(approval());});
  await assert.rejects(gate.getApproval('../api/approvals'),AgentGateValidationError);
  await assert.rejects(gate.waitForApproval(approvalId,{pollIntervalMs:0}),AgentGateValidationError);
  await assert.rejects(gate.waitForApproval(approvalId,{timeoutMs:Infinity}),AgentGateValidationError);assert.equal(calls,0);
});
test('constructor rejects unsafe URL and header configurations without echoing secrets',()=>{
  for(const config of [{baseUrl:'http://remote.example.test'},{baseUrl:'https://user:'+key+'@example.test'},{baseUrl:'https://example.test?apiKey='+key},
    {baseUrl:'https://example.test#'+key},{baseUrl:'file:///tmp'},{apiKey:key+'\r\nInjected: yes'},{maxRetries:6},{requestTimeoutMs:NaN}]){
    assert.throws(()=>client(async()=>response(evaluation()),config),error=>error instanceof AgentGateValidationError&&!inspect(error).includes(key));
  }
});
test('request validation rejects missing keys, unknown fields, cycles, nonfinite values and excessive sizes',async()=>{
  let calls=0;const gate=client(async()=>{calls++;return response(evaluation());});const cycle={};cycle.self=cycle;
  for(const input of [{...request(),idempotencyKey:''},{...request(),organizationId:actionId},{...request(),parameters:{amount:Infinity}},
    {...request(),parameters:{nested:cycle}},{...request(),parameters:{when:new Date()}},{...request(),parameters:{large:'a'.repeat(32769)}}]) await assert.rejects(gate.evaluate(input),AgentGateValidationError);
  assert.equal(calls,0);
});
test('request accessors cannot alter validated data while serializing',async()=>{
  const input=request();Object.defineProperty(input.parameters,'amount',{get:()=>750,enumerable:true});
  await assert.rejects(client(async()=>response(evaluation())).evaluate(input),AgentGateValidationError);
});
test('array accessors and custom serialization cannot bypass JSON validation',async()=>{
  let reads=0;const accessor=[750];Object.defineProperty(accessor,'0',{get(){reads++;return 750;},enumerable:true});
  const custom=[750];custom.toJSON=()=>({amount:Infinity});
  const iterator=[Infinity];iterator[Symbol.iterator]=function*(){yield 750;};
  for(const nested of [accessor,custom,iterator]) await assert.rejects(client(async()=>response(evaluation())).evaluate({...request(),parameters:{nested}}),AgentGateValidationError);
  assert.equal(reads,0);
});
test('clients do not expose credentials through serialization or inspection',()=>{
  const gate=client(async()=>response(evaluation()),{baseUrl:'https://example.test/'+key});
  assert.deepEqual(Object.keys(gate),[]);assert.equal(inspect(gate).includes(key),false);assert.equal(JSON.stringify(gate).includes(key),false);
});
test('package imports work through ESM and native CommonJS',()=>{
  const common=createRequire(import.meta.url)('@agentgate/sdk');assert.equal(common.AgentGate,AgentGate);
});
test('native fetch refuses redirects and never sends the key to their target',async()=>{
  let leaked=0;
  const target=createServer((_req,res)=>{leaked++;res.end('{}');});await new Promise(resolve=>target.listen(0,'127.0.0.1',resolve));
  const source=createServer((_req,res)=>{res.writeHead(307,{Location:`http://127.0.0.1:${target.address().port}/credentials`});res.end();});await new Promise(resolve=>source.listen(0,'127.0.0.1',resolve));
  try {
    const gate=new AgentGate({apiKey:key,baseUrl:`http://127.0.0.1:${source.address().port}`,maxRetries:0});
    await assert.rejects(gate.evaluate(request()),AgentGateNetworkError);assert.equal(leaked,0);
  } finally {source.closeAllConnections();target.closeAllConnections();await Promise.all([new Promise(resolve=>source.close(resolve)),new Promise(resolve=>target.close(resolve))]);}
});
