import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdir, mkdtemp, writeFile, readFile, copyFile, rm, realpath } from 'node:fs/promises';
import { resolve, join, relative, isAbsolute } from 'node:path';
import { fileURLToPath } from 'node:url';

const packageRoot=fileURLToPath(new URL('../',import.meta.url));
const verificationRoot=resolve(packageRoot,'../../.local-verification');
await mkdir(verificationRoot,{recursive:true});
const npm=process.env.npm_execpath;
if(!npm) throw new Error('Run this check with npm run test:package.');
const temporary=await mkdtemp(join(verificationRoot,'sdk-package-'));
function runNpm(args,cwd,stdio='pipe'){return execFileSync(process.execPath,[npm,...args],{cwd,stdio,encoding:'utf8'});}
try {
  const packed=JSON.parse(runNpm(['pack','--json','--pack-destination',temporary],packageRoot))[0];
  assert.equal(packed.name,'@agentgate/sdk');
  for(const file of packed.files) assert(file.path==='package.json'||file.path==='README.md'||/^dist\/.+\.(?:js|d\.ts)$/.test(file.path),'Unexpected packed file: '+file.path);
  assert(packed.files.some(file=>file.path==='dist/index.js'));assert(packed.files.some(file=>file.path==='dist/index.d.ts'));
  const consumer=join(temporary,'consumer');await mkdir(consumer);
  await writeFile(join(consumer,'package.json'),JSON.stringify({private:true,type:'module'}));
  runNpm(['install',join(temporary,packed.filename),'--offline','--ignore-scripts','--no-audit','--no-fund','--package-lock=false'],consumer);
  await writeFile(join(consumer,'smoke.mjs'),`import { AgentGate } from '@agentgate/sdk';
    import { createRequire } from 'node:module';
    import assert from 'node:assert/strict';
    assert.equal(createRequire(import.meta.url)('@agentgate/sdk').AgentGate,AgentGate);
    const gate=new AgentGate({apiKey:'synthetic-consumer-key',baseUrl:'http://localhost:5000',fetch:async()=>new Response(JSON.stringify({actionId:'11111111-1111-4111-8111-111111111111',decision:'allow',status:'approved',reason:'Allowed',testEvaluation:false,matchedPolicyId:null,matchedPolicyName:null,reviewerRole:null,riskLevel:'Low',policyUpdatedAt:null,approvalId:null,approvalStatus:null,approvalExpiresAt:null}),{headers:{'content-type':'application/json'}})});
    assert.equal((await gate.evaluate({action:'refund',resource:{type:'customer',id:'consumer'},parameters:{amount:50,currency:'USD'},idempotencyKey:'consumer'})).decision,'allow');`);
  execFileSync(process.execPath,['smoke.mjs'],{cwd:consumer,stdio:'inherit'});
  for(const name of ['consumer.mts','consumer.cts','tsconfig.json']) await copyFile(join(packageRoot,'test/types',name),join(consumer,name));
  execFileSync(process.execPath,[join(packageRoot,'node_modules/typescript/bin/tsc'),'-p',join(consumer,'tsconfig.json')],{cwd:consumer,stdio:'inherit'});
  const manifest=JSON.parse(await readFile(join(consumer,'node_modules/@agentgate/sdk/package.json'),'utf8'));
  assert.equal(Object.keys(manifest.dependencies||{}).length,0);
  console.log('Packed SDK verified: isolated offline installation, ESM/CommonJS imports, TypeScript consumers, and no runtime dependencies.');
} finally {
  const root=await realpath(verificationRoot);const target=await realpath(temporary);const boundary=relative(root,target);
  if(boundary&&!boundary.startsWith('..')&&!isAbsolute(boundary)) await rm(target,{recursive:true,force:true});
  else console.error('Refusing to remove a directory outside local verification.');
}
