import { AgentGate, type ApprovalResult } from '@agentgate/sdk';
const gate = new AgentGate({apiKey:'synthetic-agent-key',baseUrl:'http://localhost:5000'});
const poll: Promise<ApprovalResult> = gate.getApproval('11111111-1111-4111-8111-111111111111');
void poll;
