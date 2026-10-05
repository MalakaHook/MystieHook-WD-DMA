import {test} from 'node:test';
import assert from 'node:assert/strict';
import {createRadarServer} from '../server.mjs';
import {mkdtemp,mkdir,writeFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {readFile} from 'node:fs/promises';

test('local sessions, freshness, manual tiles, password cookies and cleanup',async()=>{
  const tileRoot=await mkdtemp(path.join(tmpdir(),'wardog-local-'));
  const source=await readFile(new URL('../server.mjs',import.meta.url),'utf8');
  const version=source.match(/"bakurani":"([a-f0-9]{64})"/)[1];
  const directory=path.join(tileRoot,'bakurani',version,'0','0');
  await mkdir(directory,{recursive:true});await writeFile(path.join(directory,'0.webp'),'tile-fixture');
  const key='a'.repeat(64);
  const server=createRadarServer({publisherKey:key,origin:'http://127.0.0.1:3080',tileRoot,test:true});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const base=`http://127.0.0.1:${server.address().port}`;
  const post=(url,body,token=key,extra={})=>fetch(base+url,{method:'POST',headers:{'Content-Type':'application/json',Authorization:`Bearer ${token}`,...extra},body:JSON.stringify(body)});
  try{
    assert.equal((await post('/api/sessions',{},'wrong')).status,401);
    const created=await post('/api/sessions',{timingVersion:1});assert.equal(created.status,201);
    const room=await created.json(), route='/api/sessions/'+room.id;
    assert.equal(room.id.length,7);
    assert.equal((await fetch(base+'/',{redirect:'manual'})).headers.get('location'),'/'+room.id);
    const frame={map:0,local:{x:0,y:0},players:[],vehicles:[],ageMs:0,vehicleAgeMs:0,sentAt:Date.now(),timing:{nonce:room.timingNonce,elapsedMs:0}};
    const upload=await post(route+'/frame',frame,room.token);assert.equal(upload.status,200);
    const result=await upload.json();assert.equal(result.status,'live');
    assert.equal(await (await fetch(base+route+'/tiles/bakurani/0/0/0.webp')).text(),'tile-fixture');
    assert.equal((await (await post(route+'/frame',frame,room.token)).json()).status,'timing_invalid');
    const stale=await post(route+'/frame',{...frame,ageMs:250},room.token);
    assert.equal((await stale.json()).status,'source_stale');
    assert.equal((await post(route+'/password',{password:'test-password'},room.token)).status,200);
    assert.equal((await fetch(base+route)).status,401);
    const login=await post(route+'/login',{password:'test-password'},'',{Origin:'http://127.0.0.1:3080'});
    assert.equal(login.status,200);assert.ok(!login.headers.get('set-cookie').includes('; Secure'));
    assert.equal((await fetch(base+route,{headers:{Cookie:login.headers.get('set-cookie').split(';')[0]}})).status,200);
    assert.equal((await fetch(base+route,{method:'DELETE',headers:{Authorization:`Bearer ${room.token}`}})).status,200);
    assert.equal((await fetch(base+route)).status,404);
  }finally{server.closeAllConnections();await new Promise(resolve=>server.close(resolve));await rm(tileRoot,{recursive:true,force:true});}
});