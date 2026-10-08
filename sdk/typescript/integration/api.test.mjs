import { test } from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { AgentGate, AgentGateHttpError, AgentGateTimeoutError, AgentGateAbortError } from '@agentgate/sdk';

const url = process.env.AGENTGATE_SDK_TEST_URL;
test('SDK uses real AgentGate action, approval, policy, and audit contracts', {skip: !url}, async t => {
  if (process.env.AGENTGATE_SDK_TEST_MODE !== 'isolated') throw new Error('SDK integration tests require an isolated API. Run scripts/test-sdk.ps1.');
  let ownerToken;
  async function api(path, body, token=ownerToken, method=body?'POST':'GET') {
    const response=await fetch(url+path,{method,headers:{'X-AgentGate-Client':'dashboard','Content-Type':'application/json',...(token?{Authorization:'Bearer '+token}:{})},...(body?{body:JSON.stringify(body)}:{})});
    assert(response.ok,`Integration setup failed with HTTP ${response.status} at ${path}`);
    return response.status===204?undefined:response.json();
  }
  const session=await api('/api/auth/register',{email:`sdk-${randomUUID()}@example.test`,password:'synthetic-sdk-integration-password',name:'SDK verifier',organizationName:'SDK verification'},null);ownerToken=session.accessToken;
  const agent=await api('/api/agents',{name:'SDK agent',environment:'Development',version:'1.0.0'});
  const generated=await api(`/api/agents/${agent.id}/keys`,{name:'Isolated SDK key'});
  const gate=new AgentGate({apiKey:generated.key,baseUrl:url,maxRetries:0});
  await api('/api/policies/seed-refund-demo',{});
  const request=(key,amount=750)=>({action:'refund',resource:{type:'customer',id:'SDK-TEST'},parameters:{amount,currency:'USD'},idempotencyKey:key});
  await t.test('allows and denials match real policies',async()=>{
    assert.equal((await gate.evaluate(request('allow',50))).decision,'allow');assert.equal((await gate.evaluate(request('deny',15000))).decision,'deny');
  });
  await t.test('idempotency reuses an action and changed payloads return a typed 409',async()=>{
    const first=await gate.evaluate(request('idempotent'));const second=await gate.evaluate(request('idempotent'));assert.equal(first.actionId,second.actionId);
    await assert.rejects(gate.evaluate(request('idempotent',751)),error=>error instanceof AgentGateHttpError&&error.statusCode===409);
    const timeline=await api(`/api/actions/${first.actionId}/timeline`);assert.equal(timeline.total,4);
  });
  await t.test('polling waits for a real human approval without executing an action',async()=>{
    const result=await gate.evaluate(request('approve'));assert.equal((await gate.getApproval(result.approvalId)).status,'pending');
    const waiting=gate.waitForApproval(result.approvalId,{pollIntervalMs:100,timeoutMs:3000});
    await api(`/api/approvals/${result.approvalId}/approve`,{comment:'SDK integration check'});
    const final=await waiting;assert.equal(final.status,'approved');assert.equal(final.actionStatus,'approved');
    assert.equal((await gate.evaluate(request('approve'))).approvalStatus,'approved');
    const action=await api(`/api/actions/${result.actionId}`);assert.equal(action.executedAt,null);
    const timeline=await api(`/api/actions/${result.actionId}/timeline`);assert.equal(timeline.items.at(-1).eventType,'approval.approved');
  });
  await t.test('rejection is returned as a terminal server status',async()=>{
    const result=await gate.evaluate(request('reject'));await api(`/api/approvals/${result.approvalId}/reject`,{comment:'Reject test'});
    assert.equal((await gate.waitForApproval(result.approvalId)).status,'rejected');
  });
  await t.test('timeouts and cancellation leave pending server state unchanged',async()=>{
    const result=await gate.evaluate(request('timeout'));await assert.rejects(gate.waitForApproval(result.approvalId,{timeoutMs:30,pollIntervalMs:100}),AgentGateTimeoutError);
    assert.equal((await gate.getApproval(result.approvalId)).status,'pending');
    const controller=new AbortController();controller.abort();await assert.rejects(gate.waitForApproval(result.approvalId,{signal:controller.signal}),AgentGateAbortError);
  });
  await t.test('agent and tenant isolation remain enforced through the SDK',async()=>{
    const own=await gate.evaluate(request('private'));
    const other=await api('/api/agents',{name:'Other SDK agent',environment:'Development',version:'1.0.0'});const otherKey=await api(`/api/agents/${other.id}/keys`,{name:'Other key'});
    const otherGate=new AgentGate({apiKey:otherKey.key,baseUrl:url});await assert.rejects(otherGate.getApproval(own.approvalId),error=>error instanceof AgentGateHttpError&&error.statusCode===404);
    const otherSession=await api('/api/auth/register',{email:`other-${randomUUID()}@example.test`,password:'synthetic-sdk-integration-password',name:'Other tenant',organizationName:'Other tenant'},null);
    const otherAgent=await api('/api/agents',{name:'Foreign agent',environment:'Development',version:'1.0.0'},otherSession.accessToken);
    const foreignKey=await api(`/api/agents/${otherAgent.id}/keys`,{name:'Foreign key'},otherSession.accessToken);
    await assert.rejects(new AgentGate({apiKey:foreignKey.key,baseUrl:url}).getApproval(own.approvalId),error=>error instanceof AgentGateHttpError&&error.statusCode===404);
  });
  await t.test('disabled agents receive typed authentication failures',async()=>{
    await api(`/api/agents/${agent.id}/disable`,{},ownerToken);await assert.rejects(gate.evaluate(request('disabled')),error=>error instanceof AgentGateHttpError&&error.statusCode===401);
  });
});
