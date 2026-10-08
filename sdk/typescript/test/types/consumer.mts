import { AgentGate, AgentGateHttpError, type EvaluationResult, type EvaluateRequest, type ResolvedApproval } from '@agentgate/sdk';
const request: EvaluateRequest = {action:'refund',resource:{type:'customer',id:'CUS-102'},parameters:{amount:750,currency:'USD'},idempotencyKey:'refund-1'};
const gate = new AgentGate({apiKey:'synthetic-agent-key',baseUrl:'http://localhost:5000'});
async function consume() {
  const result: EvaluationResult = await gate.evaluate(request);
  if (result.decision === 'review') {
    const approval: ResolvedApproval = await gate.waitForApproval(result.approvalId);
    const terminal: 'approved' | 'rejected' | 'expired' | 'cancelled' = approval.status;
    return terminal;
  }
  // @ts-expect-error Non-review responses have no approval UUID.
  await gate.getApproval(result.approvalId);
  return result.status;
}
// @ts-expect-error The idempotency key is mandatory.
gate.evaluate({action:'refund',resource:{type:'customer',id:'CUS-102'},parameters:{amount:1,currency:'USD'}});
// @ts-expect-error JSON parameters cannot contain functions.
gate.evaluate({...request, parameters:{callback:()=>{}}});
// @ts-expect-error Only the specified decisions are valid.
const invalid: EvaluationResult['decision'] = 'execute';
const httpError = new AgentGateHttpError(429, 1000);
void consume; void invalid; void httpError.retryAfterMs;
