// A server-issued timing anchor plus elapsed client monotonic time bounds network
// age without comparing Windows/VPS wall clocks. The bound includes the previous
// response journey and the current upload journey, so it is conservative.
export function transportAge(timing,anchor,now){
  if(!anchor||!timing||timing.nonce!==anchor.nonce||!Number.isFinite(timing.elapsedMs)||timing.elapsedMs<0||timing.elapsedMs>10000)return null;
  const interval=now-anchor.issued;
  if(interval<0||interval>10000)return null;
  const age=interval-timing.elapsedMs;
  if(age < -2)return null;
  return Math.max(0,age)+2;
}
