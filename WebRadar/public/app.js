import {RadarMotion,radarVisible,RadarFollow,facingDirection,mapRotation,rotateVector} from '/motion.js';
const motion=new RadarMotion(),follow=new RadarFollow();
const $=id=>document.getElementById(id), canvas=$('canvas'),ctx=canvas.getContext('2d');
const preferenceIds=['players','vehicles','teammates','playerNames','vehicleNames','smooth','range','keepCentered','rotateMap'];
try{const saved=JSON.parse(localStorage.getItem('mystie-radar-display')||'{}');for(const id of preferenceIds){if(id==='range'){if(Number.isFinite(saved.range))$(id).value=String(Math.max(100,Math.min(30000,saved.range)));}else if(typeof saved[id]==='boolean')$(id).checked=saved[id];}}catch{}
function savePreferences(){try{localStorage.setItem('mystie-radar-display',JSON.stringify(Object.fromEntries(preferenceIds.map(key=>[key,key==='range'?Math.max(100,Math.min(30000,Number($(key).value)||2000)):$(key).checked]))));}catch{}}
for(const id of preferenceIds)$(id).addEventListener('change',savePreferences);
const session=location.pathname.split('/')[1],validId=/^(?:[A-Za-z0-9_-]{7}|[a-f0-9]{32})$/.test(session),api=`/api/sessions/${session}`;
const maps=[{id:'bakurani',name:'Bakurani',x:-1632000,y:-2040000,size:1632000,max:6},{id:'ozeti',name:'Ozeti',x:-1616000,y:-1616000,size:1632000,max:7},{id:'zestafona',name:'Zestafona',x:-819200,y:-819200,size:1638400,max:7}];
let selected=0,zoom=1,cx=.5,cy=.5,width=1,height=1,scale=1,frame=null,expires=0,ended=false,locked=false,sequence=0;
let labels=[],tiles=new Map(),pending=0,tick=0,drag=null,artillery=false,weaponData=[],points=maps.map(()=>({gun:null,target:null}));
let fetchState='waiting_upload',receipt=null,receiptSeen=0,vehicleExpires=0;
let rotation=0;
const receiptLabel=document.createElement('span');receiptLabel.id='receiptStatus';document.querySelector('footer').prepend(receiptLabel);
function waitingMessage(){
 if(frame?.local&&!mapMatches())return 'Waiting for the shared map selection.';
 const state=fetchState==='live'&&performance.now()>=expires?'transport_stale':fetchState;
 return ({source_stale:'Desktop game positions are stale',source_missing:'Waiting for desktop game positions',transport_stale:'Network delay exceeded the 1 second web display limit',timing_invalid:'Synchronizing publisher timing',network_error:'Cannot reach radar server',waiting_upload:'Waiting for the desktop app to upload positions'})[state]||'Waiting for fresh game positions';
}
// Manual selection overrides only image projection, never source freshness.
const mapMatches=()=>!$('autoMap').checked||frame?.map===selected;
const uv=p=>({x:(p.x-maps[selected].x)/maps[selected].size,y:(p.y-maps[selected].y)/maps[selected].size});
const world=p=>({x:maps[selected].x+p.x*maps[selected].size,y:maps[selected].y+p.y*maps[selected].size});
const screen=p=>{const v=rotateVector((p.x-cx)*scale,(p.y-cy)*scale,rotation);return {x:width/2+v.x,y:height/2+v.y};};
const fromScreen=(x,y)=>{const v=rotateVector((x-width/2)/scale,(y-height/2)/scale,-rotation);return {x:cx+v.x,y:cy+v.y};};
function setMap(value){if(value===selected)return;selected=value;motion.clear();follow.reset();zoom=1;cx=cy=.5;tiles.clear();sequence++;$('map').value=String(value);$('mapTitle').textContent=maps[value].name;}
function fit(){zoom=1;cx=cy=.5;follow.reset();$('keepCentered').checked=false;$('follow').checked=false;savePreferences();}
$('map').onchange=()=>{$('autoMap').checked=false;setMap(Number($('map').value));};
$('autoMap').onchange=()=>{motion.clear();follow.reset();if($('autoMap').checked&&frame?.map>=0&&frame.map<3)setMap(frame.map);};
$('fit').onclick=fit;
function center(){follow.reset();track();}
function localPose(){return $('smooth').checked?motion.sample('slocal',frame.local,performance.now()):frame.local;}
function track(){if(frame?.local&&mapMatches()&&performance.now()<expires&&!locked&&!ended){const local=uv(localPose()),p=$('keepCentered').checked?local:follow.center(local);cx=p.x;cy=p.y;}}
function panTo(x,y){if($('keepCentered').checked)return;if($('follow').checked)follow.pan(x-cx,y-cy);cx=x;cy=y;}
$('follow').onchange=()=>{if($('follow').checked)center();};$('center').onclick=center;$('zoomIn').onclick=()=>zoom=Math.min(128,zoom*1.5);$('zoomOut').onclick=()=>zoom=Math.max(1,zoom/1.5);
$('center').onclick=()=>{$('keepCentered').checked=true;center();savePreferences();};
$('keepCentered').onchange=()=>{follow.reset();if($('keepCentered').checked)track();};
$('radarTab').onclick=()=>setTab(false);$('artilleryTab').onclick=()=>setTab(true);
function setTab(value){artillery=value;$('artilleryPanel').hidden=!value;$('radarTab').classList.toggle('active',!value);$('artilleryTab').classList.toggle('active',value);if(!value)$('mode').value='pan';}
$('clear').onclick=()=>points[selected]={gun:null,target:null};
$('useLocal').onclick=()=>{if(frame?.local&&performance.now()<expires)points[selected].gun={...frame.local};};
canvas.onwheel=e=>{e.preventDefault();const r=canvas.getBoundingClientRect(),x=e.clientX-r.left,y=e.clientY-r.top,before=fromScreen(x,y);zoom=Math.max(1,Math.min(128,zoom*Math.exp(-e.deltaY*.0015)));scale=Math.min(width,height)*zoom;const after=fromScreen(x,y);panTo(cx+before.x-after.x,cy+before.y-after.y);};
canvas.onpointerdown=e=>{canvas.setPointerCapture(e.pointerId);drag={x:e.clientX,y:e.clientY,startX:e.clientX,startY:e.clientY};};
canvas.onpointermove=e=>{const r=canvas.getBoundingClientRect(),w=world(fromScreen(e.clientX-r.left,e.clientY-r.top));$('coords').textContent=`X ${(w.x/100).toFixed(0)} m  ·  Y ${(w.y/100).toFixed(0)} m`;if(drag){const d=rotateVector((e.clientX-drag.x)/scale,(e.clientY-drag.y)/scale,-rotation);panTo(cx-d.x,cy-d.y);drag.x=e.clientX;drag.y=e.clientY;}};
canvas.onpointerup=e=>{if(drag&&Math.hypot(e.clientX-drag.startX,e.clientY-drag.startY)<5&&artillery&&$('mode').value!=='pan'){const r=canvas.getBoundingClientRect();points[selected][$('mode').value]=world(fromScreen(e.clientX-r.left,e.clientY-r.top));}drag=null;};canvas.onpointercancel=()=>drag=null;
// Plan the complete viewport before requesting anything: render order must never
// make a still-visible tile look evictable. Include every ancestor, not just L-1.
function tilePlan(a,b){
 let level=Math.min(maps[selected].max,Math.max(0,Math.ceil(Math.log2(scale/256))));
 const bounds=z=>{const n=2**z;return {minX:Math.max(0,Math.floor(a.x*n)),maxX:Math.min(n-1,Math.ceil(b.x*n)-1),minY:Math.max(0,Math.floor(a.y*n)),maxY:Math.min(n-1,Math.ceil(b.y*n)-1)};};
 let box=bounds(level);
 while(level>0&&Math.max(0,box.maxX-box.minX+1)*Math.max(0,box.maxY-box.minY+1)>64)box=bounds(--level);
 const wanted=new Map();
 for(let z=0;z<=level;z++){const shift=level-z;for(let y=box.minY;y<=box.maxY;y++)for(let x=box.minX;x<=box.maxX;x++){const tx=Math.floor(x/2**shift),ty=Math.floor(y/2**shift),key=`${z}/${tx}/${ty}`;if(!wanted.has(key))wanted.set(key,{z,x:tx,y:ty});}}
 return wanted;
}
function tileVisible(t,a,b){const n=2**t.z;return t.x/n<b.x&&(t.x+1)/n>a.x&&t.y/n<b.y&&(t.y+1)/n>a.y;}
function tile(z,x,y,wanted,a,b){const key=`${z}/${x}/${y}`,old=tiles.get(key);
 if(old){if(old.done&&!old.image&&performance.now()>=old.retryAt)tiles.delete(key);else return;}
 if(pending>=8||locked||ended||!validId)return;
 if(tiles.size>=128){
  const candidates=[...tiles.entries()].filter(([k,v])=>v.done&&!wanted.has(k));
  // Off-screen entries go first; obsolete detail is expendable only after its
  // lower-resolution ancestors have been protected by the full viewport plan.
  candidates.sort((aEntry,bEntry)=>Number(tileVisible(aEntry[1],a,b))-Number(tileVisible(bEntry[1],a,b))||aEntry[1].used-bEntry[1].used);
  const victim=candidates[0];if(victim)tiles.delete(victim[0]);else return;
 }
 const image=new Image(),entry={z,x,y,image:null,done:false,used:tick,retryAt:0},generation=sequence;tiles.set(key,entry);pending++;
 image.onload=()=>{pending--;if(sequence===generation&&tiles.get(key)===entry){entry.image=image;entry.done=true;}};
 image.onerror=()=>{pending--;if(sequence===generation&&tiles.get(key)===entry){entry.done=true;entry.retryAt=performance.now()+5000;}};
 image.src=`${api}/tiles/${maps[selected].id}/${z}/${x}/${y}.webp`;
}
function drawTiles(){
 const corners=[[0,0],[width,0],[0,height],[width,height]].map(([x,y])=>fromScreen(x,y)),a={x:Math.min(...corners.map(p=>p.x)),y:Math.min(...corners.map(p=>p.y))},b={x:Math.max(...corners.map(p=>p.x)),y:Math.max(...corners.map(p=>p.y))};
 const wanted=tilePlan(a,b);
 // Keep a ready fallback from eviction even while changing detail levels.
 for(const t of wanted.values())tile(t.z,t.x,t.y,wanted,a,b);
 // Render all ready visible levels from coarse to fine. Previously loaded detail
 // remains visible across zoom thresholds; pending/failed tiles draw nothing over it.
 for(const entry of [...tiles.values()].filter(t=>t.image&&tileVisible(t,a,b)).sort((a,b)=>a.z-b.z)){
  entry.used=tick;const n=2**entry.z,p=screen({x:entry.x/n,y:entry.y/n}),tileSize=scale/n;
  ctx.save();ctx.translate(p.x,p.y);ctx.rotate(rotation);ctx.drawImage(entry.image,0,0,tileSize+.5,tileSize+.5);ctx.restore();
 }
}function marker(p,color,shape,label){const at=screen(uv(p));if(at.x<0||at.y<0||at.x>width||at.y>height)return;ctx.fillStyle=color;ctx.strokeStyle='#101218';ctx.lineWidth=2;ctx.beginPath();if(shape==='square')ctx.rect(at.x-4,at.y-4,8,8);else ctx.arc(at.x,at.y,shape==='self'?6:4,0,Math.PI*2);ctx.fill();ctx.stroke();const facing=shape!=='square'?facingDirection(p.facingYaw):null,direction=facing?rotateVector(facing.x,facing.y,rotation):null;if(direction){ctx.beginPath();ctx.moveTo(at.x+direction.x*4,at.y+direction.y*4);ctx.lineTo(at.x+direction.x*14,at.y+direction.y*14);ctx.strokeStyle='#101218';ctx.lineWidth=4;ctx.stroke();ctx.strokeStyle=color;ctx.lineWidth=2;ctx.stroke();}ctx.strokeStyle='#101218';if(label){ctx.font='11px Segoe UI,sans-serif';const box={x:at.x+9,y:at.y-8,w:ctx.measureText(label).width+4,h:14};if(labels.some(b=>box.x<b.x+b.w&&box.x+box.w>b.x&&box.y<b.y+b.h&&box.y+box.h>b.y))return;labels.push(box);ctx.lineWidth=3;ctx.strokeText(label,at.x+9,at.y+4);ctx.fillStyle='#f3f3f6';ctx.fillText(label,at.x+9,at.y+4);}}
function interpolate(table,d){if(!table?.length||d<table[0][0]||d>table.at(-1)[0])return null;const i=table.findIndex(p=>p[0]>=d);if(i===0)return table[0][1];const a=table[i-1],b=table[i];return a[1]+(b[1]-a[1])*(d-a[0])/(b[0]-a[0]);}
function drawArtillery(){const p=points[selected],w=weaponData[Number($('weapon').value)];if(p.gun){marker(p.gun,'#96d278','self','Artillery');if(w){const at=screen(uv(p.gun));ctx.beginPath();ctx.strokeStyle='#96d27888';ctx.lineWidth=1;ctx.arc(at.x,at.y,w.max*100/maps[selected].size*scale,0,Math.PI*2);ctx.stroke();}}if(p.target)marker(p.target,'#f24059','self','Target');
 if(!p.gun||!p.target){$('solution').textContent='Place artillery and target points.';return;}const a=screen(uv(p.gun)),b=screen(uv(p.target));ctx.beginPath();ctx.strokeStyle='#f2405999';ctx.moveTo(a.x,a.y);ctx.lineTo(b.x,b.y);ctx.stroke();const dx=(p.target.x-p.gun.x)/100,dy=(p.target.y-p.gun.y)/100,d=Math.hypot(dx,dy),bearing=(Math.atan2(dx,-dy)*180/Math.PI+360)%360;
 let solution=`${d.toFixed(0)} m · ${bearing.toFixed(1)}°`;if(w){const low=d>=w.min&&d<=w.max?interpolate(w.low,d):null,high=d>=w.min&&d<=w.max?interpolate(w.high,d):null;solution+=low===null&&high===null?'\nOut of supported range':`\nLOW ${low?.toFixed(1)??'—'} MIL · HIGH ${high?.toFixed(1)??'—'} MIL`;}else solution+='\nFiring tables unavailable';$('solution').textContent=solution;}
function render(){requestAnimationFrame(render);tick++;labels=[];const rect=canvas.getBoundingClientRect(),dpr=Math.min(devicePixelRatio||1,2);width=rect.width;height=rect.height;if(canvas.width!==Math.round(width*dpr)||canvas.height!==Math.round(height*dpr)){canvas.width=Math.round(width*dpr);canvas.height=Math.round(height*dpr);}ctx.setTransform(dpr,0,0,dpr,0,0);ctx.clearRect(0,0,width,height);scale=Math.min(width,height)*zoom;
 const fresh=performance.now()<expires&&frame?.local&&mapMatches()&&!locked&&!ended;rotation=mapRotation(fresh?localPose().facingYaw:null,$('rotateMap').checked);if(fresh&&($('follow').checked||$('keepCentered').checked))track();drawTiles();
 if(!fresh)motion.clear();const range=Math.max(100,Math.min(30000,Number($('range').value)||2000));const visible=p=>radarVisible(p,frame?.local,range,$('teammates').checked);const pose=(prefix,p)=>$('smooth').checked?motion.sample(prefix+(p.id||'local'),p,performance.now()):p;if(fresh){if($('players').checked)for(const p of frame.players.filter(visible))marker(pose('p',p),({Friendly:'#96d278',Enemy:'#ff555f',Unknown:'#ebcd50'})[p.team]||'#ebcd50','dot',$('playerNames').checked?p.name:'');if(performance.now()<vehicleExpires&&$('vehicles').checked)for(const v of frame.vehicles.filter(visible))marker(pose('v',v),'#ff913c','square',$('vehicleNames').checked?v.name:'');marker(pose('s',frame.local),'#50aaff','self','You');}
 if(artillery)drawArtillery();$('emptyState').hidden=!!fresh;$('emptyState').textContent=ended?'Session ended':locked?'Password required':waitingMessage();const showReceipt=!locked&&!ended&&receipt;$('playerCount').textContent=showReceipt&&receipt.players!==null?receipt.players:'—';$('vehicleCount').textContent=showReceipt&&receipt.vehicles!==null?receipt.vehicles:'—';receiptLabel.textContent=showReceipt&&receipt.uploads?('Upload '+receipt.uploads+' · Last received counts · '+(receipt.receivedAgoMs===null?'No fresh source':((receipt.receivedAgoMs+performance.now()-receiptSeen)/1000).toFixed(1)+'s ago')):'No game data received yet';$('signal').classList.toggle('live',!!fresh);$('status').textContent=ended?'Session ended':locked?'Locked':fresh?($('autoMap').checked?'Live · connected':'Live · manual map: '+maps[selected].name):'Waiting for game data';$('zoomValue').textContent=`${zoom.toFixed(1)}×`;}
async function poll(){if(ended||!validId)return;const start=performance.now();try{const response=await fetch(api,{cache:'no-store',signal:AbortSignal.timeout(4000)});if(response.status===404){ended=true;expires=0;return;}if(response.status===401){locked=true;expires=0;if(!$('login').open)$('login').showModal();return;}if(!response.ok)throw Error();const data=await response.json();if(frame?.map!==data.map){motion.clear();follow.reset();}frame=data;receipt=data.receipt||null;receiptSeen=performance.now();vehicleExpires=receiptSeen+Math.max(0,(data.vehicleRemainingMs||0)-(receiptSeen-start));fetchState=data.status||'waiting_upload';expires=performance.now()+Math.max(0,data.remainingMs-(performance.now()-start));locked=false;if($('autoMap').checked&&data.map>=0&&data.map<3)setMap(data.map);motion.update(data,data.receipt?.uploads,performance.now());}catch{expires=0;fetchState='network_error';}finally{if(!ended)setTimeout(poll,100);}}
$('loginForm').onsubmit=async e=>{e.preventDefault();$('loginError').textContent='Checking…';try{const response=await fetch(`${api}/login`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({password:$('password').value})});if(!response.ok){const data=await response.json();throw Error(data.error||'Unable to unlock session');}$('password').value='';$('loginError').textContent='';locked=false;$('login').close();}catch(error){$('loginError').textContent=error.message;}};
$('login').addEventListener('cancel',e=>e.preventDefault());
fetch('/artillery.json').then(r=>r.json()).then(data=>weaponData=data).catch(()=>{});
if(!validId){ended=true;$('emptyState').textContent='Open a session link from the desktop app';}
render();poll();



