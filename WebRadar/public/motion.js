// Bounded interpolation of matching anonymous contacts. Never extrapolates or
// changes source/transport expiry; callers clear on stale data and map changes.
export class RadarMotion {
 constructor(){this.entries=new Map();}
 clear(){this.entries.clear();this.sequence=undefined;this.updated=undefined;}
 sample(key,p,now){const e=this.entries.get(key);if(!e)return p;const t=Math.max(0,Math.min(1,(now-e.time)/e.duration));return {...p,x:e.from.x+(e.to.x-e.from.x)*t,y:e.from.y+(e.to.y-e.from.y)*t,facingYaw:Number.isFinite(p.facingYaw)?interpolateYaw(e.from.facingYaw,e.to.facingYaw,t):null};}
 update(frame,sequence,now){
  if(this.sequence===sequence)return;this.sequence=sequence;
  const duration=this.updated===undefined?100:Math.max(50,Math.min(250,now-this.updated));this.updated=now;
  const keep=new Set();
  for(const [prefix,list] of [['p',frame.players||[]],['v',frame.vehicles||[]],['s',frame.local?[{...frame.local,id:'local'}]:[]]])
   for(const p of list){if(!p.id)continue;const key=prefix+p.id;keep.add(key);const previous=this.entries.get(key),from=this.sample(key,p,now);
    const snap=!previous||Math.hypot(p.x-previous.to.x,p.y-previous.to.y)>10000;
    this.entries.set(key,{from:snap?p:from,to:p,time:now,duration});}
  for(const key of this.entries.keys())if(!keep.has(key))this.entries.delete(key);
 }
}
export function radarVisible(p,local,range,teammates){return !!local&&(teammates||p.team!=='Friendly')&&Math.hypot(p.x-local.x,p.y-local.y)<=range*100;}
export function interpolateYaw(from,to,t){if(!Number.isFinite(to))return null;if(!Number.isFinite(from))return to;return (from+((to-from+540)%360-180)*t+360)%360;}
// Map image X/Y follow world X/Y: zero yaw points right, +90 points down.
export function facingDirection(yaw){return Number.isFinite(yaw)?{x:Math.cos(yaw*Math.PI/180),y:Math.sin(yaw*Math.PI/180)}:null;}
export function mapRotation(yaw,enabled){return enabled&&Number.isFinite(yaw)?-Math.PI/2-yaw*Math.PI/180:0;}
export function rotateVector(x,y,angle){const c=Math.cos(angle),s=Math.sin(angle);return {x:x*c-y*s,y:x*s+y*c};}
export class RadarFollow {
 constructor(){this.reset();}
 reset(){this.x=0;this.y=0;}
 pan(dx,dy){this.x+=dx;this.y+=dy;}
 center(local){return {x:local.x+this.x,y:local.y+this.y};}
}
