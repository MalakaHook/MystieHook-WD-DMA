import http from 'node:http';
import { randomBytes, scrypt as scryptCallback, timingSafeEqual, createHash } from 'node:crypto';
import { promisify } from 'node:util';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import {transportAge} from './freshness.mjs';

const scrypt = promisify(scryptCallback);
const here = path.dirname(fileURLToPath(import.meta.url));
const digest = s => createHash('sha256').update(String(s)).digest();
const equal = (a,b) => timingSafeEqual(digest(a),digest(b));
const secret = () => randomBytes(32).toString('hex');
const mapVersions = {"bakurani":"a120377d9e0ad3d09c1bb36643094dc2ac083854338a2889a3ac748e1f937615","ozeti":"dc8fc3fe5ddb169aea36f97c9bd8465d3be18e5a635f30fa70c792bfe6517c0d","zestafona":"d021201845a81cefc1942da60c7fa178a4007a978c0d584f46ee3cfe54cbccfe"};
const mapIds = ['bakurani','ozeti','zestafona'];
const empty = (status='waiting_upload') => ({map:-1, local:null, players:[], vehicles:[], remainingMs:0,status});
const newTiming = () => ({nonce:randomBytes(16).toString('hex'),issued:performance.now()});

export function validateFrame(input) {
  if (!input || !Number.isInteger(input.map) || input.map < -1 || input.map > 2) throw Error('Invalid map');
  const point = p => {
    if (!p || !Number.isFinite(p.x) || !Number.isFinite(p.y) || Math.abs(p.x)>1e8 || Math.abs(p.y)>1e8) throw Error('Invalid position');
    return {x:p.x,y:p.y};
  };
  const contacts = (items,max,vehicle) => {
    if (!Array.isArray(items) || items.length>max) throw Error('Too many contacts');
    return items.map(p => ({...point(p),name:typeof p.name==='string' ? p.name.slice(0,64) : '',
      id:typeof p.id==='string'&&/^[a-f0-9]{32}$/.test(p.id)?p.id:null,
      facingYaw:!vehicle&&Number.isFinite(p.facingYaw)&&p.facingYaw>=0&&p.facingYaw<360?p.facingYaw:null,
      team:['Friendly','Enemy','Unknown'].includes(p.team) ? p.team : 'Unknown'}));
  };
  if (!Number.isFinite(input.ageMs) || input.ageMs<0 || input.ageMs>=250) return empty();
  if (!Number.isFinite(input.vehicleAgeMs) || input.vehicleAgeMs<0) throw Error('Invalid vehicle age');
  return {map:input.map,local:input.local ? {...point(input.local),facingYaw:Number.isFinite(input.local.facingYaw)&&input.local.facingYaw>=0&&input.local.facingYaw<360?input.local.facingYaw:null} : null,
    players:contacts(input.players,1024,false),vehicles:input.vehicleAgeMs<250 ? contacts(input.vehicles,512,true) : [],
    remainingMs:1000-input.ageMs, vehicleRemainingMs:input.vehicleAgeMs<250?Math.max(0,1000-input.vehicleAgeMs):0};
}

export function createRadarServer({publisherKey, origin, tileRoot, test=false, maxRooms=32}) {

  if (!publisherKey || publisherKey.length<32) throw Error('Local process token required');
  const publicUrl=new URL(origin);
  if (publicUrl.protocol!=='http:' || publicUrl.hostname!=='127.0.0.1') throw Error('Loopback HTTP origin required');
  const rooms=new Map(), limits=new Map();
  let hashing=0;
  const json=(res,status,value)=>{res.writeHead(status,{'Content-Type':'application/json','Cache-Control':'no-store'});res.end(JSON.stringify(value));};
  const sweep=()=>{const now=Date.now();for(const [id,r] of rooms)if(now-r.touched>120000)rooms.delete(id);for(const [k,v]of limits)if(now>v.until)limits.delete(k);};
  const timer=setInterval(sweep,30000);timer.unref();
  const limit=(key,max)=>{
    const now=Date.now();let item=limits.get(key);
    if(!item||now>item.until){if(limits.size>=4096)sweep();if(limits.size>=4096)return false;item={count:0,until:now+60000};limits.set(key,item);}
    return ++item.count<=max;
  };
  const body=async req=>{
    if(!req.headers['content-type']?.startsWith('application/json'))throw Error('JSON required');
    const parts=[];let size=0;
    for await(const part of req){size+=part.length;if(size>262144)throw Error('Request too large');parts.push(part);}
    return JSON.parse(Buffer.concat(parts).toString());
  };
  const passwordHash=async password=>{
    if(typeof password!=='string'||password.length>128||(password.length>0&&password.length<8))throw Error('Password must be empty or 8–128 characters');
    if(!password)return null;
    if(hashing>=4)throw Error('Busy');
    hashing++;try{const salt=secret();return {salt,hash:await scrypt(password,salt,32)};}finally{hashing--;}
  };
  const publisher=(req,r)=>equal(req.headers.authorization||'',`Bearer ${r.token}`);
  const viewer=(req,id,r)=>{
    if(!r.password)return true;
    const token=(req.headers.cookie||'').split(';').map(x=>x.trim()).find(x=>x.startsWith(`radar_${id}=`))?.split('=')[1];
    if(!token)return false;
    const expiry=r.grants.get(digest(token).toString('hex'));
    return expiry && expiry>Date.now();
  };
  const server=http.createServer(async(req,res)=>{
    res.setHeader('X-Content-Type-Options','nosniff');res.setHeader('Referrer-Policy','no-referrer');
    res.setHeader('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'");
    try{
      const url=new URL(req.url,origin), route=url.pathname;
      const ip=test?req.socket.remoteAddress:String(req.headers['x-real-ip']||req.socket.remoteAddress);
      if(req.method==='GET'&&route==='/healthz')return json(res,200,{ok:true,instance:process.env.RADAR_INSTANCE});
      if(req.method==='GET'&&route==='/'&&rooms.size){res.writeHead(302,{Location:'/'+[...rooms.keys()].at(-1)});return res.end();}
      if(req.method==='POST'&&route==='/api/sessions'){
        if(!equal(req.headers.authorization||'',`Bearer ${publisherKey}`))return json(res,401,{error:'Local publisher required'});
        if(!limit(`create:${ip}`,10))return json(res,429,{error:'Try later'});
        sweep();if(rooms.size>=maxRooms)return json(res,503,{error:'Session capacity reached'});
        const input=await body(req),password=await passwordHash(input.password??'');
        if(rooms.size>=maxRooms)return json(res,503,{error:'Session capacity reached'});
        let id;do{id=randomBytes(6).toString('base64url').slice(0,7);}while(rooms.has(id));
        const token=secret();
        const timing=newTiming();
        rooms.set(id,{token,password,grants:new Map(),frame:empty(),received:0,touched:Date.now(),timing,requireTiming:input.timingVersion===1});
        return json(res,201,{id,token,url:`${publicUrl.origin}/${id}`,timingNonce:timing.nonce});
      }
      const match=route.match(/^\/api\/sessions\/([A-Za-z0-9_-]{7}|[a-f0-9]{32})(?:\/(frame|login|password|tiles\/([a-z]+)\/(\d+)\/(\d+)\/(\d+)\.webp))?$/);
      if(match){
        const [,id,action,map,zs,xs,ys]=match,r=rooms.get(id);
        if(!r||Date.now()-r.touched>120000){rooms.delete(id);return json(res,404,{error:'Session ended'});}
        if(req.method==='DELETE'&&!action){if(!publisher(req,r))return json(res,401,{});rooms.delete(id);return json(res,200,{ok:true});}
        if(req.method==='POST'&&action==='frame'){
          if(!publisher(req,r))return json(res,401,{});
          if(!limit(`publish:${id}`,900))return json(res,429,{});
          const input=await body(req),frame=validateFrame(input);
          // New clients use monotonic elapsed time. Legacy clients retain their
          // original strict wall-clock policy until their desktop is upgraded.
          const legacyTransit=Date.now()-input.sentAt;
          const transit=r.requireTiming?transportAge(input.timing,r.timing,performance.now()):legacyTransit>=-100?Math.max(0,legacyTransit):null;
          const status=input.ageMs>=250?'source_stale':transit===null||!Number.isFinite(transit)||transit<0?'timing_invalid':transit>=frame.remainingMs?'transport_stale':!frame.local?'source_missing':'live';
          const receiptNow=performance.now();
          r.uploads=(r.uploads||0)+1;r.uploadStatus=status;
          if(Number.isFinite(input.ageMs)&&input.ageMs>=0&&input.ageMs<250)
            r.lastData={players:frame.players.length,vehicles:frame.vehicles.length,sourceAgeMs:input.ageMs,
              transportMs:Number.isFinite(transit)?Math.max(0,transit):null,at:receiptNow};
          r.frame=status==='live'
            ? {...frame,status,remainingMs:Math.max(0,frame.remainingMs-transit),vehicleRemainingMs:Math.max(0,(frame.vehicleRemainingMs||0)-transit)} : empty(status);
          r.received=performance.now();r.touched=Date.now();r.timing=newTiming();
          return json(res,200,{ok:true,status,timingNonce:r.timing.nonce,players:r.lastData?.players??null,vehicles:r.lastData?.vehicles??null,uploads:r.uploads});
        }
        if(req.method==='POST'&&action==='password'){
          if(!publisher(req,r))return json(res,401,{});
          if(!limit(`password:${id}`,10))return json(res,429,{});
          const input=await body(req);r.password=await passwordHash(input.password??'');r.grants.clear();return json(res,200,{ok:true});
        }
        if(req.method==='POST'&&action==='login'){
          if(req.headers.origin!==publicUrl.origin)return json(res,403,{});
          if(!limit(`login:${id}:${ip}`,5))return json(res,429,{error:'Too many attempts. Try in a minute.'});
          const input=await body(req);
          if(typeof input.password!=='string'||input.password.length>128)return json(res,400,{});
          const current=r.password;
          if(current){
            if(hashing>=4)return json(res,503,{});
            hashing++;let hash;try{hash=await scrypt(input.password,current.salt,32);}finally{hashing--;}
            if(current!==r.password||!timingSafeEqual(hash,current.hash))return json(res,401,{error:'Incorrect password'});
          }
          for(const [key,time] of r.grants)if(time<Date.now())r.grants.delete(key);
          if(r.grants.size>=100)return json(res,503,{error:'Viewer capacity reached'});
          const grant=secret();r.grants.set(digest(grant).toString('hex'),Date.now()+3600000);
          res.setHeader('Set-Cookie',`radar_${id}=${grant}; HttpOnly; SameSite=Strict; Path=/api/sessions/${id}; Max-Age=3600`);
          return json(res,200,{ok:true});
        }
        if(req.method==='GET'){
          if(!viewer(req,id,r))return json(res,401,{locked:true});
          if(!action){
            const age=performance.now()-r.received;
            const view=age>=r.frame.remainingMs?empty(r.frame.status==='live'?'transport_stale':r.frame.status):{...r.frame,remainingMs:r.frame.remainingMs-age,
              vehicleRemainingMs:Math.max(0,(r.frame.vehicleRemainingMs||0)-age),vehicles:age<(r.frame.vehicleRemainingMs||0)?r.frame.vehicles:[]};
            return json(res,200,{...view,receipt:{uploads:r.uploads||0,status:r.uploadStatus||'waiting_upload',
              players:r.lastData?.players??null,vehicles:r.lastData?.vehicles??null,
              receivedAgoMs:r.lastData?Math.max(0,performance.now()-r.lastData.at):null,
              sourceAgeMs:r.lastData?.sourceAgeMs??null,transportMs:r.lastData?.transportMs??null}});
          }
          if(map){
            const z=Number(zs),x=Number(xs),y=Number(ys),max=map==='bakurani'?6:7;
            if(!mapIds.includes(map)||z<0||z>max||x<0||y<0||x>=2**z||y>=2**z)return json(res,404,{});
            try{const data=await readFile(path.join(tileRoot,map,mapVersions[map],String(z),String(x),`${y}.webp`));
              res.writeHead(200,{'Content-Type':'image/webp','Cache-Control':'private, max-age=300'});return res.end(data);
            }catch{return json(res,404,{});}
          }
        }
        return json(res,405,{});
      }
      const assets={'/app.js':['app.js','text/javascript'],'/motion.js':['motion.js','text/javascript'],'/style.css':['style.css','text/css'],'/artillery.json':['artillery.json','application/json'],'/brand.png':['brand.png','image/png']};
      if(req.method==='GET'&&(route==='/'||/^\/(?:[A-Za-z0-9_-]{7}|[a-f0-9]{32})\/?$/.test(route)||assets[route])){
        const [file,type]=assets[route]||['index.html','text/html'];
        const data=await readFile(path.join(here,'public',file));res.writeHead(200,{'Content-Type':type,'Cache-Control':'no-cache'});return res.end(data);
      }
      json(res,404,{error:'Not found'});
    }catch{if(!res.headersSent)json(res,400,{error:'Invalid request'});else res.end();}
  });
  server.requestTimeout=5000;server.headersTimeout=5000;
  server.on('close',()=>clearInterval(timer));
  return server;
}

if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  if(Number(process.versions.node.split('.')[0])<22)throw Error('Node.js 22 or newer required');
  const server=createRadarServer({publisherKey:process.env.PUBLISHER_KEY,origin:'http://127.0.0.1:3080',tileRoot:process.env.MAP_TILE_ROOT||path.join(process.env.LOCALAPPDATA||'','MystieHook','Maps')});
  server.listen(3080,'127.0.0.1',()=>console.log('Local radar listening on http://127.0.0.1:3080'));
}