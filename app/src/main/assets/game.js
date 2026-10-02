(() => {
'use strict';

const canvas = document.getElementById('game');
const ctx = canvas.getContext('2d', { alpha: false });
const TAU = Math.PI * 2;
const SAVE_KEY = 'recoil_rivals_save_v1';

let W = 390, H = 844, DPR = 1, last = performance.now();
let state = 'menu';
let currentLevel = 1;
let player = null;
let enemies = [];
let bullets = [];
let particles = [];
let floatingTexts = [];
let obstacles = [];
let screenShake = 0;
let timeScale = 1;
let finishTimer = 0;
let levelStartTime = 0;
let lastEnemyCount = 0;
let audioCtx = null;
let muted = false;
let tutorialStep = 0;
let buttons = [];
let menuGunAngle = -0.25;

const COLORS = {
  bg: '#080A12', panel: '#111524', panel2: '#171C2F', text: '#F8FAFF', muted: '#9AA4C0',
  purple: '#6C5CE7', purple2: '#A29BFE', cyan: '#00D2D3', red: '#FF5D73', yellow: '#FFD166',
  green: '#53E08B', enemy: '#FF6B81', white: '#FFFFFF', wall: '#242B40', wallEdge: '#3B4564'
};

const weapons = {
  pistol: {
    name: 'STARTER PISTOL', unlock: 1, damage: 1, recoil: 240, bulletSpeed: 690, cooldown: 0.24,
    pellets: 1, spread: 0, spin: 2.1, color: '#6C5CE7', accent: '#A29BFE', bulletColor: '#E6E0FF',
    description: 'Balanced recoil and fire rate.'
  },
  revolver: {
    name: 'REVOLVER', unlock: 3, damage: 2, recoil: 350, bulletSpeed: 780, cooldown: 0.48,
    pellets: 1, spread: 0, spin: 3.0, ricochet: 1, color: '#FF9F43', accent: '#FFD166', bulletColor: '#FFE2A9',
    description: 'Heavy recoil. Bullets ricochet once.'
  },
  shotgun: {
    name: 'SHOTGUN', unlock: 5, damage: 1, recoil: 470, bulletSpeed: 620, cooldown: 0.64,
    pellets: 5, spread: 0.28, spin: 3.6, color: '#00B894', accent: '#55EFC4', bulletColor: '#B8FFF0',
    description: 'Huge recoil and a five-pellet blast.'
  }
};

const ENEMY_STATS = {
  standard: { name:'PISTOL', hp:1, cooldown:1.65, bulletSpeed:315, damage:1, color:'#FF6B81', size:22 },
  fast:     { name:'FAST', hp:2, cooldown:0.78, bulletSpeed:360, damage:1, color:'#FF7675', size:20 },
  heavy:    { name:'CANNON', hp:3, cooldown:2.2, bulletSpeed:250, damage:1, color:'#E17055', size:28, heavy:true },
  shield:   { name:'SHIELD', hp:2, cooldown:1.75, bulletSpeed:300, damage:1, color:'#D63031', size:24, shield:true }
};

const LEVELS = [
  { enemies:[['standard',.72,.29]], obs:[] },
  { enemies:[['standard',.70,.28]], obs:[['platform',.43,.53,.36,.035]] },
  { enemies:[['standard',.72,.25],['standard',.27,.34]], obs:[] },
  { enemies:[['fast',.72,.27],['standard',.25,.30]], obs:[['platform',.34,.50,.32,.03]] },
  { enemies:[['heavy',.72,.25]], obs:[['platform',.21,.45,.25,.03],['platform',.57,.58,.24,.03]] },
  { enemies:[['fast',.75,.22],['fast',.25,.33]], obs:[['wall',.47,.28,.06,.27]] },
  { enemies:[['shield',.72,.25],['standard',.26,.25]], obs:[['platform',.30,.55,.40,.035]] },
  { enemies:[['heavy',.77,.24],['fast',.24,.38]], obs:[['wall',.48,.20,.055,.28],['platform',.12,.57,.30,.03]] },
  { enemies:[['shield',.77,.23],['fast',.22,.25],['standard',.52,.40]], obs:[['platform',.32,.55,.36,.03]] },
  { boss:true, enemies:[['heavy',.50,.24],['fast',.22,.34],['fast',.78,.34]], obs:[['platform',.18,.55,.24,.03],['platform',.58,.55,.24,.03]] }
];

let save = loadSave();

function defaultSave(){
  return { levelUnlocked:1, coins:0, xp:0, playerLevel:1, selectedWeapon:'pistol', stars:{}, sound:true, haptics:true };
}
function loadSave(){
  try { return Object.assign(defaultSave(), JSON.parse(localStorage.getItem(SAVE_KEY) || '{}')); }
  catch { return defaultSave(); }
}
function saveGame(){ localStorage.setItem(SAVE_KEY, JSON.stringify(save)); }

function resize(){
  DPR = Math.min(window.devicePixelRatio || 1, 2.5);
  W = Math.max(300, innerWidth); H = Math.max(500, innerHeight);
  canvas.width = Math.floor(W*DPR); canvas.height = Math.floor(H*DPR);
  canvas.style.width = W+'px'; canvas.style.height = H+'px';
  ctx.setTransform(DPR,0,0,DPR,0,0);
}
window.addEventListener('resize', resize); resize();

function clamp(v,a,b){ return Math.max(a,Math.min(b,v)); }
function lerp(a,b,t){ return a+(b-a)*t; }
function dist2(a,b){ const x=a.x-b.x,y=a.y-b.y; return x*x+y*y; }
function rr(a,b){ return a + Math.random()*(b-a); }
function roundRectPath(x,y,w,h,r){
  r=Math.min(r,w/2,h/2); ctx.beginPath(); ctx.moveTo(x+r,y); ctx.arcTo(x+w,y,x+w,y+h,r); ctx.arcTo(x+w,y+h,x,y+h,r); ctx.arcTo(x,y+h,x,y,r); ctx.arcTo(x,y,x+w,y,r); ctx.closePath();
}
function fillRound(x,y,w,h,r,c){ ctx.fillStyle=c; roundRectPath(x,y,w,h,r); ctx.fill(); }
function strokeRound(x,y,w,h,r,c,l=1){ ctx.strokeStyle=c; ctx.lineWidth=l; roundRectPath(x,y,w,h,r); ctx.stroke(); }
function poly(points,fill,stroke=null,line=1){
  ctx.beginPath(); ctx.moveTo(points[0][0],points[0][1]);
  for(let i=1;i<points.length;i++) ctx.lineTo(points[i][0],points[i][1]);
  ctx.closePath(); if(fill){ctx.fillStyle=fill;ctx.fill();} if(stroke){ctx.strokeStyle=stroke;ctx.lineWidth=line;ctx.stroke();}
}
function glassPanel(x,y,w,h,r=18,alpha=.88){
  const g=ctx.createLinearGradient(x,y,x,y+h);
  g.addColorStop(0,`rgba(30,38,66,${alpha})`);
  g.addColorStop(1,`rgba(10,14,28,${alpha})`);
  fillRound(x,y,w,h,r,g);
  strokeRound(x+.5,y+.5,w-1,h-1,r,'rgba(255,255,255,.10)',1);
  ctx.save(); roundRectPath(x+2,y+2,w-4,h*.42,r-2); ctx.clip();
  const hi=ctx.createLinearGradient(0,y,0,y+h*.42); hi.addColorStop(0,'rgba(255,255,255,.08)');hi.addColorStop(1,'rgba(255,255,255,0)');
  ctx.fillStyle=hi;ctx.fillRect(x,y,w,h*.42);ctx.restore();
}
function glowDot(x,y,r,c,alpha=.7){
  ctx.save();const g=ctx.createRadialGradient(x,y,0,x,y,r*3);g.addColorStop(0,c);g.addColorStop(.28,c);g.addColorStop(1,'rgba(0,0,0,0)');
  ctx.globalAlpha=alpha;ctx.fillStyle=g;ctx.beginPath();ctx.arc(x,y,r*3,0,TAU);ctx.fill();ctx.restore();
}
function text(t,x,y,size=20,color=COLORS.text,align='center',weight=800){
  ctx.fillStyle=color; ctx.textAlign=align; ctx.textBaseline='middle'; ctx.font=`${weight} ${size}px system-ui,-apple-system,Segoe UI,Roboto,sans-serif`; ctx.fillText(t,x,y);
}

function sfx(type){
  if(!save.sound || muted) return;
  try{
    if(!audioCtx) audioCtx = new (window.AudioContext||window.webkitAudioContext)();
    if(audioCtx.state==='suspended') audioCtx.resume();
    const now=audioCtx.currentTime, osc=audioCtx.createOscillator(), g=audioCtx.createGain();
    osc.connect(g); g.connect(audioCtx.destination);
    let f1=180,f2=80,d=.09,vol=.05,wave='square';
    if(type==='shot'){f1=180;f2=75;d=.07;vol=.055;}
    if(type==='revolver'){f1=110;f2=55;d=.12;vol=.075;}
    if(type==='shotgun'){f1=90;f2=38;d=.16;vol=.095;wave='sawtooth';}
    if(type==='hit'){f1=440;f2=220;d=.055;vol=.04;wave='triangle';}
    if(type==='hurt'){f1=130;f2=70;d=.18;vol=.07;wave='sawtooth';}
    if(type==='win'){f1=420;f2=820;d=.28;vol=.055;wave='triangle';}
    if(type==='click'){f1=350;f2=420;d=.035;vol=.025;wave='sine';}
    osc.type=wave; osc.frequency.setValueAtTime(f1,now); osc.frequency.exponentialRampToValueAtTime(Math.max(20,f2),now+d);
    g.gain.setValueAtTime(vol,now); g.gain.exponentialRampToValueAtTime(.0001,now+d);
    osc.start(now); osc.stop(now+d+.01);
  }catch{}
}
function vibrate(ms){ if(save.haptics && navigator.vibrate) navigator.vibrate(ms); }

function addButton(id,label,x,y,w,h,opts={}){
  const b={id,label,x,y,w,h,enabled:opts.enabled!==false,sub:opts.sub||'',accent:opts.accent||COLORS.purple}; buttons.push(b); return b;
}
function drawButton(b){
  const c=b.enabled?b.accent:'#31384A';
  const pressed=false;
  ctx.save();
  ctx.shadowColor=b.enabled?c:'transparent'; ctx.shadowBlur=b.enabled?18:0; ctx.shadowOffsetY=4;
  const g=ctx.createLinearGradient(b.x,b.y,b.x,b.y+b.h);
  if(b.enabled){g.addColorStop(0,'rgba(255,255,255,.11)');g.addColorStop(.12,c);g.addColorStop(1,'#111728');}
  else {g.addColorStop(0,'#303648');g.addColorStop(1,'#171A23');}
  fillRound(b.x,b.y,b.w,b.h,16,g);
  ctx.shadowBlur=0;ctx.shadowOffsetY=0;
  strokeRound(b.x+.75,b.y+.75,b.w-1.5,b.h-1.5,16,b.enabled?'rgba(255,255,255,.15)':'rgba(255,255,255,.05)',1.5);
  const inner=ctx.createLinearGradient(0,b.y+4,0,b.y+b.h-5);
  inner.addColorStop(0,'rgba(8,12,24,.10)');inner.addColorStop(1,'rgba(5,8,18,.72)');
  fillRound(b.x+4,b.y+4,b.w-8,b.h-8,12,inner);
  ctx.fillStyle='rgba(255,255,255,.11)';fillRound(b.x+10,b.y+7,b.w-20,2,1,'rgba(255,255,255,.11)');
  text(b.label,b.x+b.w/2,b.y+b.h/2-(b.sub?7:0),Math.min(20,b.h*.34),b.enabled?COLORS.text:'#777E90');
  if(b.sub) text(b.sub,b.x+b.w/2,b.y+b.h/2+17,11,b.enabled?COLORS.muted:'#565C6C','center',700);
  ctx.restore();
}

function hitButton(x,y){ return buttons.find(b=>b.enabled&&x>=b.x&&x<=b.x+b.w&&y>=b.y&&y<=b.y+b.h); }

function drawGun(x,y,angle,weaponKey,scale=1,enemy=false,shield=false){
  const wp=weapons[weaponKey]||weapons.pistol;
  const base=enemy?'#D64E62':wp.color;
  const accent=enemy?'#FF8392':wp.accent;
  ctx.save();ctx.translate(x,y);ctx.rotate(angle);ctx.scale(scale,scale);

  // Contact shadow gives the floating weapon a real object silhouette.
  ctx.save();ctx.rotate(-angle);ctx.scale(1/scale,1/scale);
  ctx.globalAlpha=.24;ctx.filter='blur(7px)';ctx.fillStyle='#000';
  ctx.beginPath();ctx.ellipse(2,26,34,10,0,0,TAU);ctx.fill();
  ctx.restore();

  ctx.shadowColor=accent;ctx.shadowBlur=12;

  if(weaponKey==='shotgun'&&!enemy){
    // Long barrel, top rib and receiver.
    let g=ctx.createLinearGradient(-38,-13,46,12);g.addColorStop(0,'#111827');g.addColorStop(.45,'#334155');g.addColorStop(1,'#0B1220');
    fillRound(-34,-10,72,17,5,g); strokeRound(-34,-10,72,17,5,'rgba(255,255,255,.18)',1);
    const bg=ctx.createLinearGradient(28,-7,62,6);bg.addColorStop(0,'#26364B');bg.addColorStop(1,'#0A101B');
    fillRound(28,-6,37,9,4,bg);fillRound(39,-9,27,3,1,'#51627B');
    // pump
    let pg=ctx.createLinearGradient(-18,6,10,19);pg.addColorStop(0,accent);pg.addColorStop(1,base);
    fillRound(-11,5,28,12,5,pg);
    for(let i=0;i<4;i++) fillRound(-6+i*6,7,2,8,1,'rgba(0,0,0,.3)');
    // grip + stock
    poly([[-25,5],[-8,7],[-14,31],[-30,29]],base,'rgba(255,255,255,.15)',1);
    poly([[-35,-5],[-60,-2],[-69,10],[-38,11]],'#202A3C','rgba(255,255,255,.10)',1);
    fillRound(-61,0,18,7,3,'#3B475E');
    glowDot(61,-1,2,'#FFF0B0',.55);
  } else if(weaponKey==='revolver'&&!enemy){
    // barrel
    let bg=ctx.createLinearGradient(-8,-11,50,7);bg.addColorStop(0,'#273247');bg.addColorStop(.5,'#56657D');bg.addColorStop(1,'#121927');
    fillRound(-5,-10,52,14,4,bg);strokeRound(-5,-10,52,14,4,'rgba(255,255,255,.18)',1);
    fillRound(35,-7,18,7,3,'#8A96A8');
    // cylinder
    let cg=ctx.createRadialGradient(-10,-2,2,-10,-2,15);cg.addColorStop(0,'#697890');cg.addColorStop(.55,'#354055');cg.addColorStop(1,'#151C29');
    ctx.fillStyle=cg;ctx.beginPath();ctx.arc(-11,-2,14,0,TAU);ctx.fill();ctx.strokeStyle='rgba(255,255,255,.18)';ctx.lineWidth=1;ctx.stroke();
    for(let i=0;i<6;i++){const a=i*TAU/6;ctx.fillStyle='#111725';ctx.beginPath();ctx.arc(-11+Math.cos(a)*7,-2+Math.sin(a)*7,2.2,0,TAU);ctx.fill();}
    // frame + grip
    fillRound(-28,7,32,9,4,base);
    let gg=ctx.createLinearGradient(-25,12,-8,36);gg.addColorStop(0,accent);gg.addColorStop(1,'#6E331B');
    poly([[-23,10],[-6,12],[-11,36],[-27,31]],gg,'rgba(255,255,255,.14)',1);
    // hammer / sight
    poly([[-27,-9],[-19,-16],[-14,-9]],'#7E899B');
    fillRound(13,-14,8,3,1,'#A8B1C0');
    glowDot(49,-3,2,'#FFD79A',.55);
  } else {
    // Premium compact pistol / enemy sidearm.
    const slide=ctx.createLinearGradient(-28,-12,42,7);
    slide.addColorStop(0,enemy?'#762D3A':'#1C2434');slide.addColorStop(.28,enemy?'#E45C71':'#53617A');slide.addColorStop(.52,enemy?'#B53E52':'#2C374B');slide.addColorStop(1,'#0C111C');
    poly([[-28,-11],[37,-11],[45,-5],[39,6],[-26,6]],slide,'rgba(255,255,255,.20)',1);
    // slide bevel
    poly([[-24,-8],[34,-8],[39,-4],[-23,-4]],'rgba(255,255,255,.10)');
    // muzzle and front sight
    fillRound(35,-8,13,10,3,'#101722');fillRound(39,-6,7,6,3,'#02050A');
    fillRound(25,-15,7,3,1,accent);
    // frame
    const fg=ctx.createLinearGradient(-22,3,24,17);fg.addColorStop(0,accent);fg.addColorStop(.45,base);fg.addColorStop(1,'#242A37');
    poly([[-24,4],[26,4],[18,15],[-14,15]],fg,'rgba(255,255,255,.16)',1);
    // trigger guard
    ctx.strokeStyle=enemy?'#A83C4E':base;ctx.lineWidth=4;ctx.beginPath();ctx.arc(4,14,8,.05,Math.PI*.95);ctx.stroke();
    // trigger
    ctx.strokeStyle='#B6BDCB';ctx.lineWidth=2;ctx.beginPath();ctx.moveTo(4,8);ctx.lineTo(1,16);ctx.stroke();
    // grip with inset rubber panels
    const gg=ctx.createLinearGradient(-18,12,-2,42);gg.addColorStop(0,enemy?'#8E3446':accent);gg.addColorStop(.55,enemy?'#5E2532':base);gg.addColorStop(1,'#111725');
    poly([[-17,12],[6,12],[1,38],[-14,41],[-24,18]],gg,'rgba(255,255,255,.14)',1);
    fillRound(-14,18,11,17,4,'rgba(5,9,17,.38)');
    for(let i=0;i<3;i++) fillRound(-12,20+i*5,7,2,1,'rgba(255,255,255,.08)');
    glowDot(44,-3,2,'#FFF0B0',.55);
  }

  // Mechanical detail and premium rim light.
  ctx.shadowBlur=0;
  ctx.fillStyle='rgba(255,255,255,.20)';fillRound(-18,-9,14,2,1,'rgba(255,255,255,.18)');
  if(shield){
    ctx.save();ctx.shadowColor='#66D9FF';ctx.shadowBlur=16;ctx.strokeStyle='#69D6FF';ctx.lineWidth=5;
    ctx.beginPath();ctx.arc(0,0,34,-1.22,1.22);ctx.stroke();ctx.restore();
  }
  ctx.restore();
}

function startLevel(n){
  currentLevel=clamp(n,1,LEVELS.length); const cfg=LEVELS[currentLevel-1];
  state='playing'; buttons=[]; bullets=[]; particles=[]; floatingTexts=[]; obstacles=[]; enemies=[]; finishTimer=0; timeScale=1; tutorialStep=0;
  const arena = arenaRect();
  const key = weapons[save.selectedWeapon] && currentLevel>=weapons[save.selectedWeapon].unlock ? save.selectedWeapon : 'pistol';
  save.selectedWeapon=key;
  player={x:arena.x+arena.w*.5,y:arena.y+arena.h*.77,vx:0,vy:0,angle:-Math.PI/2,av:.42,r:20,hearts:3,maxHearts:3,lastShot:-99,damageTaken:0,invuln:0,weapon:key};
  cfg.enemies.forEach((e,i)=>{
    const st=ENEMY_STATS[e[0]];
    enemies.push({type:e[0],x:arena.x+arena.w*e[1],y:arena.y+arena.h*e[2],vx:0,vy:0,angle:Math.PI/2,av:(i%2?-.22:.22),r:st.size,hp:st.hp,maxHp:st.hp,shot:rr(.3,1.2),flash:0,dead:false});
  });
  cfg.obs.forEach(o=> obstacles.push({type:o[0],x:arena.x+arena.w*o[1],y:arena.y+arena.h*o[2],w:arena.w*o[3],h:arena.h*o[4]}));
  lastEnemyCount=enemies.length; levelStartTime=performance.now(); sfx('click');
}

function arenaRect(){
  const top = Math.max(74, H*.085), bottom = Math.max(30,H*.045), side=Math.max(14,W*.035);
  return {x:side,y:top,w:W-side*2,h:H-top-bottom};
}

function firePlayer(){
  if(state!=='playing'||finishTimer>0) return;
  const now=performance.now()/1000, wp=weapons[player.weapon];
  if(now-player.lastShot<wp.cooldown) return;
  player.lastShot=now;
  const ca=Math.cos(player.angle), sa=Math.sin(player.angle);
  const count=wp.pellets;
  for(let i=0;i<count;i++){
    const t=count===1?0:(i/(count-1)-.5)*2;
    const a=player.angle+t*wp.spread;
    bullets.push({x:player.x+Math.cos(a)*36,y:player.y+Math.sin(a)*36,vx:Math.cos(a)*wp.bulletSpeed,vy:Math.sin(a)*wp.bulletSpeed,r:count>1?3.3:4.2,from:'player',damage:wp.damage,color:wp.bulletColor,life:2.4,bounces:wp.ricochet||0});
  }
  player.vx-=ca*wp.recoil; player.vy-=sa*wp.recoil;
  const torque = wp.spin * (Math.sin(player.angle*1.7)>=0?1:-1);
  player.av += torque;
  muzzle(player.x+ca*34,player.y+sa*34,player.angle,wp.color);
  screenShake=Math.max(screenShake, wp===weapons.shotgun?7:3.5);
  sfx(player.weapon==='revolver'?'revolver':player.weapon==='shotgun'?'shotgun':'shot'); vibrate(player.weapon==='shotgun'?28:12);
  if(currentLevel===1 && tutorialStep===0) tutorialStep=1;
}

function fireEnemy(e){
  const st=ENEMY_STATS[e.type];
  let a=Math.atan2(player.y-e.y,player.x-e.x);
  if(e.type==='standard') a += Math.sin(performance.now()/700+e.x)*.055;
  if(e.type==='heavy') a += Math.sin(performance.now()/900)*.025;
  e.angle=lerpAngle(e.angle,a,.4);
  const speed=st.bulletSpeed;
  bullets.push({x:e.x+Math.cos(a)*34,y:e.y+Math.sin(a)*34,vx:Math.cos(a)*speed,vy:Math.sin(a)*speed,r:e.type==='heavy'?7:4.5,from:'enemy',damage:st.damage,color:e.type==='heavy'?'#FFB07C':'#FF8A9A',life:3,bounces:0});
  e.flash=.12; e.vx-=Math.cos(a)*(e.type==='heavy'?80:35); e.vy-=Math.sin(a)*(e.type==='heavy'?80:35);
  muzzle(e.x+Math.cos(a)*32,e.y+Math.sin(a)*32,a,'#FF5D73');
}
function lerpAngle(a,b,t){ let d=(b-a+Math.PI)%(TAU)-Math.PI; return a+d*t; }

function muzzle(x,y,a,c){
  for(let i=0;i<9;i++) particles.push({x,y,vx:Math.cos(a+rr(-.5,.5))*rr(70,220),vy:Math.sin(a+rr(-.5,.5))*rr(70,220),life:rr(.08,.22),max:.22,size:rr(2,6),color:i<5?'#FFF3B0':c});
}
function burst(x,y,c,n=16){
  for(let i=0;i<n;i++){const a=rr(0,TAU),s=rr(40,230);particles.push({x,y,vx:Math.cos(a)*s,vy:Math.sin(a)*s,life:rr(.18,.55),max:.55,size:rr(2,7),color:c});}
}
function floatText(label,x,y,c=COLORS.yellow){floatingTexts.push({label,x,y,life:.75,max:.75,color:c});}

function update(dt){
  dt=Math.min(dt,.035)*timeScale;
  menuGunAngle += dt*.55;
  if(state==='playing') updateGame(dt);
  updateFx(dt);
}

function updateGame(dt){
  const a=arenaRect(); const gravity=72;
  player.invuln=Math.max(0,player.invuln-dt);
  player.vy += gravity*dt; player.vx*=Math.pow(.988,dt*60); player.vy*=Math.pow(.993,dt*60); player.av*=Math.pow(.995,dt*60);
  player.x+=player.vx*dt; player.y+=player.vy*dt; player.angle+=player.av*dt;
  collideBody(player,a,.72);
  collideObstaclesBody(player);

  for(const e of enemies){
    if(e.dead) continue;
    const st=ENEMY_STATS[e.type];
    e.shot-=dt; e.flash=Math.max(0,e.flash-dt);
    const aim=Math.atan2(player.y-e.y,player.x-e.x); e.angle=lerpAngle(e.angle,aim,clamp(dt*2.2,0,1));
    e.vy += gravity*.38*dt; e.vx*=Math.pow(.985,dt*60); e.vy*=Math.pow(.99,dt*60); e.x+=e.vx*dt; e.y+=e.vy*dt;
    collideBody(e,a,.55); collideObstaclesBody(e);
    if(e.shot<=0 && finishTimer<=0){ fireEnemy(e); e.shot=st.cooldown*rr(.88,1.12); }
  }

  for(const b of bullets){
    if(b.dead) continue; b.life-=dt; if(b.life<=0){b.dead=true;continue;}
    b.x+=b.vx*dt; b.y+=b.vy*dt;
    if(b.x<a.x+b.r||b.x>a.x+a.w-b.r){
      if(b.bounces>0){b.vx*=-1;b.bounces--;b.x=clamp(b.x,a.x+b.r,a.x+a.w-b.r);burst(b.x,b.y,'#B7C5E8',4);}else b.dead=true;
    }
    if(b.y<a.y+b.r||b.y>a.y+a.h-b.r){
      if(b.bounces>0){b.vy*=-1;b.bounces--;b.y=clamp(b.y,a.y+b.r,a.y+a.h-b.r);burst(b.x,b.y,'#B7C5E8',4);}else b.dead=true;
    }
    if(b.dead) continue;
    for(const o of obstacles){
      if(circleRect(b.x,b.y,b.r,o)){
        if(b.bounces>0){
          const cx=clamp(b.x,o.x,o.x+o.w), cy=clamp(b.y,o.y,o.y+o.h); const dx=b.x-cx,dy=b.y-cy;
          if(Math.abs(dx)>Math.abs(dy)) b.vx*=-1; else b.vy*=-1; b.bounces--; burst(b.x,b.y,'#B7C5E8',4);
        } else b.dead=true;
      }
    }
    if(b.dead) continue;
    if(b.from==='player'){
      for(const e of enemies){
        if(e.dead)continue;
        if(dist2(b,e)<(b.r+e.r)*(b.r+e.r)){
          if(e.type==='shield' && shieldBlocks(e,b)){
            b.vx*=-.8;b.vy*=-.8;b.from='enemy';burst(b.x,b.y,'#74B9FF',10);sfx('hit');screenShake=2;continue;
          }
          b.dead=true;e.hp-=b.damage;e.vx+=b.vx*.08;e.vy+=b.vy*.08;burst(b.x,b.y,'#FF8092',12);sfx('hit');floatText('HIT!',e.x,e.y-34,COLORS.white);
          if(e.hp<=0) killEnemy(e,b); break;
        }
      }
    } else if(player.invuln<=0 && dist2(b,player)<(b.r+player.r)*(b.r+player.r)){
      b.dead=true; hurtPlayer(b.damage,b);
    }
  }
  bullets=bullets.filter(b=>!b.dead);

  const alive=enemies.filter(e=>!e.dead).length;
  if(alive<lastEnemyCount){ lastEnemyCount=alive; }
  if(alive===0 && finishTimer<=0){ finishTimer=.7; timeScale=.28; }
  if(finishTimer>0){
    finishTimer-=dt/Math.max(timeScale,.01);
    if(finishTimer<=0){ timeScale=1; victory(); }
  }
}

function shieldBlocks(e,b){
  const incoming=Math.atan2(b.y-e.y,b.x-e.x); let d=(incoming-e.angle+Math.PI)%(TAU)-Math.PI; return Math.abs(d)<1.15;
}
function killEnemy(e,b){
  e.dead=true;burst(e.x,e.y,'#FF5D73',26);screenShake=9;floatText('KO!',e.x,e.y-28,COLORS.yellow);vibrate(24);
}
function hurtPlayer(d,b){
  player.hearts-=d; player.damageTaken+=d; player.invuln=.7; player.vx+=b.vx*.13;player.vy+=b.vy*.13; screenShake=10; burst(player.x,player.y,'#FFFFFF',20); sfx('hurt');vibrate(55);
  floatText('-1 ♥',player.x,player.y-36,COLORS.red);
  if(player.hearts<=0){ state='defeat'; buttons=[]; timeScale=1; }
}

function collideBody(o,a,bounce=.65){
  if(o.x-o.r<a.x){o.x=a.x+o.r;o.vx=Math.abs(o.vx)*bounce;o.av+=.6;}
  if(o.x+o.r>a.x+a.w){o.x=a.x+a.w-o.r;o.vx=-Math.abs(o.vx)*bounce;o.av-=.6;}
  if(o.y-o.r<a.y){o.y=a.y+o.r;o.vy=Math.abs(o.vy)*bounce;}
  if(o.y+o.r>a.y+a.h){o.y=a.y+a.h-o.r;o.vy=-Math.abs(o.vy)*bounce;o.av+=.35;}
}
function circleRect(x,y,r,o){
  const cx=clamp(x,o.x,o.x+o.w), cy=clamp(y,o.y,o.y+o.h); const dx=x-cx,dy=y-cy; return dx*dx+dy*dy<r*r;
}
function collideObstaclesBody(o){
  for(const r of obstacles){
    if(!circleRect(o.x,o.y,o.r,r))continue;
    const left=Math.abs((o.x+o.r)-r.x), right=Math.abs(o.x-o.r-(r.x+r.w)), top=Math.abs((o.y+o.r)-r.y), bot=Math.abs(o.y-o.r-(r.y+r.h));
    const m=Math.min(left,right,top,bot);
    if(m===left){o.x=r.x-o.r;o.vx=-Math.abs(o.vx)*.6;}
    else if(m===right){o.x=r.x+r.w+o.r;o.vx=Math.abs(o.vx)*.6;}
    else if(m===top){o.y=r.y-o.r;o.vy=-Math.abs(o.vy)*.55;}
    else{o.y=r.y+r.h+o.r;o.vy=Math.abs(o.vy)*.55;}
    o.av += (o.vx>=0?1:-1)*.4;
  }
}

function updateFx(dt){
  for(const p of particles){p.life-=dt;p.x+=p.vx*dt;p.y+=p.vy*dt;p.vx*=Math.pow(.94,dt*60);p.vy*=Math.pow(.94,dt*60);}
  particles=particles.filter(p=>p.life>0);
  for(const f of floatingTexts){f.life-=dt;f.y-=35*dt;}
  floatingTexts=floatingTexts.filter(f=>f.life>0);
  screenShake=Math.max(0,screenShake-dt*28);
}

function victory(){
  state='victory'; buttons=[]; sfx('win'); vibrate(90);
  const stars=player.damageTaken===0?3:player.hearts===1?1:2;
  const reward=20+currentLevel*4+(LEVELS[currentLevel-1].boss?50:0);
  const xp=15+currentLevel*3;
  save.coins+=reward; save.xp+=xp; save.playerLevel=1+Math.floor(save.xp/120); save.stars[currentLevel]=Math.max(save.stars[currentLevel]||0,stars);
  if(currentLevel<LEVELS.length) save.levelUnlocked=Math.max(save.levelUnlocked,currentLevel+1);
  saveGame();
  stateData={stars,reward,xp};
}
let stateData={};

function draw(){
  ctx.setTransform(DPR,0,0,DPR,0,0);
  ctx.fillStyle=COLORS.bg;ctx.fillRect(0,0,W,H);buttons=[];
  let sx=0,sy=0;if(screenShake>0){sx=rr(-screenShake,screenShake);sy=rr(-screenShake,screenShake);}ctx.save();ctx.translate(sx,sy);
  if(state==='menu') drawMenu();
  else if(state==='levels') drawLevels();
  else if(state==='weapons') drawWeapons();
  else if(state==='playing') drawGame();
  else if(state==='victory') drawVictory();
  else if(state==='defeat') drawDefeat();
  ctx.restore();
  drawFx();
}

function backgroundGrid(){
  const g=ctx.createLinearGradient(0,0,0,H);
  g.addColorStop(0,'#101A33');g.addColorStop(.45,'#091122');g.addColorStop(1,'#050811');
  ctx.fillStyle=g;ctx.fillRect(0,0,W,H);

  // Atmospheric arena lights, deterministic rather than generated artwork.
  const lights=[
    [W*.17,H*.17,Math.min(W,H)*.22,'rgba(96,74,255,.16)'],
    [W*.86,H*.28,Math.min(W,H)*.25,'rgba(0,210,211,.11)'],
    [W*.52,H*.78,Math.min(W,H)*.28,'rgba(62,97,255,.08)']
  ];
  for(const L of lights){const rg=ctx.createRadialGradient(L[0],L[1],0,L[0],L[1],L[2]);rg.addColorStop(0,L[3]);rg.addColorStop(1,'rgba(0,0,0,0)');ctx.fillStyle=rg;ctx.fillRect(0,0,W,H);}

  // Far wall panels.
  ctx.save();ctx.globalAlpha=.55;
  for(let i=0;i<7;i++){
    const x=i*(W/6)-12, y=H*.18+(i%2)*9;
    const pg=ctx.createLinearGradient(x,y,x,y+H*.43);pg.addColorStop(0,'rgba(57,72,112,.16)');pg.addColorStop(1,'rgba(10,16,31,.02)');
    poly([[x,y],[x+W*.14,y-8],[x+W*.11,y+H*.40],[x-4,y+H*.43]],pg,'rgba(120,148,210,.06)',1);
  }
  ctx.restore();

  // Perspective floor grid for a deliberate 3D arena look.
  const horizon=H*.62;
  ctx.save();ctx.strokeStyle='rgba(88,116,185,.11)';ctx.lineWidth=1;
  for(let i=-7;i<=7;i++){
    const bx=W/2+i*W*.12;ctx.beginPath();ctx.moveTo(W/2,horizon);ctx.lineTo(bx,H);ctx.stroke();
  }
  for(let i=0;i<9;i++){
    const t=i/8, y=horizon+(H-horizon)*t*t;
    ctx.beginPath();ctx.moveTo(0,y);ctx.lineTo(W,y);ctx.stroke();
  }
  ctx.restore();

  // Very subtle vignette.
  const v=ctx.createRadialGradient(W/2,H*.44,Math.min(W,H)*.10,W/2,H*.44,Math.max(W,H)*.70);
  v.addColorStop(.5,'rgba(0,0,0,0)');v.addColorStop(1,'rgba(0,0,0,.50)');
  ctx.fillStyle=v;ctx.fillRect(0,0,W,H);
}

function drawTopBar(title){
  glassPanel(12,12,W-24,54,17,.91);
  glowDot(31,39,3,COLORS.yellow,.45);
  text('◆ '+save.coins,28,39,14,COLORS.yellow,'left',850);
  text(title,W/2,39,15,COLORS.text,'center',900);
  const lvl=`LV ${save.playerLevel}`; text(lvl,W-28,39,12,COLORS.cyan,'right',800);
}

function drawMenu(){
  backgroundGrid(); drawTopBar('RECOIL RIVALS');
  text('RECOIL',W/2,119,36,'#F8FBFF','center',950);
  text('RIVALS',W/2,154,36,COLORS.cyan,'center',950);
  text('MASTER THE KICK',W/2,185,11,'#AAB6D4','center',800);

  // Hero weapon showcase pod.
  const cy=H*.39;
  ctx.save();ctx.translate(W/2,cy);
  const halo=ctx.createRadialGradient(0,0,5,0,0,92);halo.addColorStop(0,'rgba(112,92,255,.24)');halo.addColorStop(.55,'rgba(0,210,211,.08)');halo.addColorStop(1,'rgba(0,0,0,0)');
  ctx.fillStyle=halo;ctx.beginPath();ctx.arc(0,0,92,0,TAU);ctx.fill();
  ctx.strokeStyle='rgba(144,159,218,.15)';ctx.lineWidth=1.5;ctx.beginPath();ctx.ellipse(0,0,94,42,0,0,TAU);ctx.stroke();
  ctx.rotate(menuGunAngle*.55-.22);drawGun(0,0,0,save.selectedWeapon,1.72);ctx.restore();

  glassPanel(W/2-105,H*.50,210,46,14,.78);
  text(weapons[save.selectedWeapon].name,W/2,H*.50+16,14,weapons[save.selectedWeapon].accent,'center',900);
  text(weapons[save.selectedWeapon].description,W/2,H*.50+32,9,COLORS.muted,'center',650);

  const bw=Math.min(330,W-44),x=(W-bw)/2;
  addButton('play','PLAY',x,H*.61,bw,68,{accent:COLORS.purple});
  addButton('weapons','ARSENAL',x,H*.71,bw,58,{accent:'#314267'});
  buttons.forEach(drawButton);

  glassPanel(x,H*.815,bw,48,14,.64);
  text(`${Math.min(save.levelUnlocked,10)} / 10 STAGES UNLOCKED`,W/2,H*.815+18,11,'#C2CCE6');
  const stars=Object.values(save.stars||{}).reduce((a,b)=>a+b,0);
  text(`★ ${stars} TOTAL`,W/2,H*.815+34,10,COLORS.yellow);
  text('RECOIL RIVALS  •  V0.2 VISUAL REMAKE',W/2,H-20,9,'#69748E','center',700);
}

function drawLevels(){
  backgroundGrid(); drawTopBar('SELECT STAGE');
  addButton('back','‹',16,76,48,48,{accent:'#232B45'});drawButton(buttons[0]);
  const cols=2,gap=14,w=(W-42-gap)/2,h=88,startY=142;
  for(let i=1;i<=10;i++){
    const c=(i-1)%cols,r=Math.floor((i-1)/cols),x=21+c*(w+gap),y=startY+r*(h+14),unlocked=i<=save.levelUnlocked;
    const stars=save.stars[i]||0;addButton('level:'+i,`STAGE ${i}`,x,y,w,h,{enabled:unlocked,accent:unlocked?(i===10?'#9B59B6':'#33406A'):'#242837',sub:unlocked?`${'★'.repeat(stars)}${'☆'.repeat(3-stars)}`:'LOCKED'});
  }
  buttons.slice(1).forEach(drawButton);
}

function drawWeapons(){
  backgroundGrid();drawTopBar('ARSENAL');
  addButton('back','‹',16,76,48,48,{accent:'#232B45'});drawButton(buttons[0]);
  let y=145;for(const [key,w] of Object.entries(weapons)){
    const unlocked=save.levelUnlocked>=w.unlock;const selected=save.selectedWeapon===key;
    fillRound(18,y,W-36,142,20,selected?'#1E2640':'#111625');strokeRound(18,y,W-36,142,20,selected?w.accent:'#262D43',selected?2:1);
    drawGun(78,y+66,-.18,key,1.05);text(w.name,142,y+34,16,unlocked?COLORS.text:'#686F82','left',900);text(w.description,142,y+60,11,unlocked?COLORS.muted:'#555B6A','left',600);
    text(`DMG ${w.damage}   RECOIL ${Math.round(w.recoil/10)}`,142,y+84,11,unlocked?w.accent:'#555B6A','left',700);
    const label=selected?'EQUIPPED':unlocked?'EQUIP':`UNLOCK STAGE ${w.unlock}`;
    addButton('weapon:'+key,label,142,y+102,Math.min(195,W-164),31,{enabled:unlocked&&!selected,accent:w.color});
    drawButton(buttons[buttons.length-1]);y+=158;
  }
}

function drawArena(){
  const a=arenaRect();
  ctx.save();
  const g=ctx.createLinearGradient(a.x,a.y,a.x,a.y+a.h);
  g.addColorStop(0,'rgba(20,31,58,.94)');g.addColorStop(.55,'rgba(9,16,32,.95)');g.addColorStop(1,'rgba(5,9,18,.98)');
  fillRound(a.x,a.y,a.w,a.h,22,g);
  strokeRound(a.x+.7,a.y+.7,a.w-1.4,a.h-1.4,22,'rgba(129,157,230,.25)',1.4);
  roundRectPath(a.x,a.y,a.w,a.h,22);ctx.clip();

  // Ceiling glow.
  const rg=ctx.createRadialGradient(a.x+a.w*.5,a.y-25,0,a.x+a.w*.5,a.y-25,a.w*.75);
  rg.addColorStop(0,'rgba(90,94,255,.17)');rg.addColorStop(.55,'rgba(0,210,211,.04)');rg.addColorStop(1,'rgba(0,0,0,0)');
  ctx.fillStyle=rg;ctx.fillRect(a.x,a.y,a.w,a.h*.6);

  // Arena perspective lines.
  const hy=a.y+a.h*.67;
  ctx.strokeStyle='rgba(116,143,207,.09)';ctx.lineWidth=1;
  for(let i=-5;i<=5;i++){ctx.beginPath();ctx.moveTo(a.x+a.w*.5,hy);ctx.lineTo(a.x+a.w*(.5+i*.17),a.y+a.h);ctx.stroke();}
  for(let i=0;i<7;i++){const t=i/6,yy=hy+(a.y+a.h-hy)*t*t;ctx.beginPath();ctx.moveTo(a.x,yy);ctx.lineTo(a.x+a.w,yy);ctx.stroke();}

  // Architectural side rails.
  const rail=ctx.createLinearGradient(a.x,0,a.x+28,0);rail.addColorStop(0,'rgba(84,105,161,.32)');rail.addColorStop(1,'rgba(20,28,47,.05)');
  ctx.fillStyle=rail;ctx.fillRect(a.x,a.y,24,a.h);ctx.save();ctx.translate(a.x+a.w,a.y);ctx.scale(-1,1);ctx.fillStyle=rail;ctx.fillRect(0,0,24,a.h);ctx.restore();

  ctx.restore();

  // 3D extruded obstacles/platforms.
  for(const o of obstacles){
    const depth=Math.max(7,o.h*.75);
    const top=ctx.createLinearGradient(o.x,o.y,o.x,o.y+o.h);top.addColorStop(0,'#667595');top.addColorStop(1,'#34415D');
    fillRound(o.x,o.y,o.w,o.h,5,top);
    poly([[o.x+5,o.y+o.h],[o.x+o.w-5,o.y+o.h],[o.x+o.w-9,o.y+o.h+depth],[o.x+9,o.y+o.h+depth]],'#161E30');
    ctx.fillStyle='rgba(255,255,255,.22)';fillRound(o.x+6,o.y+3,Math.max(0,o.w-12),2,1,'rgba(255,255,255,.22)');
    ctx.save();ctx.shadowColor=COLORS.cyan;ctx.shadowBlur=8;fillRound(o.x+5,o.y+o.h-2,Math.max(0,o.w-10),2,1,'rgba(0,210,211,.48)');ctx.restore();
  }
}

function drawGame(){
  backgroundGrid();drawArena();const a=arenaRect();

  // Projectile trails first.
  for(const b of bullets){
    ctx.save();
    const sp=Math.hypot(b.vx,b.vy)||1;
    const tx=b.x-b.vx/sp*16,ty=b.y-b.vy/sp*16;
    const grad=ctx.createLinearGradient(tx,ty,b.x,b.y);grad.addColorStop(0,'rgba(255,255,255,0)');grad.addColorStop(1,b.color);
    ctx.strokeStyle=grad;ctx.lineWidth=b.r*1.4;ctx.lineCap='round';ctx.beginPath();ctx.moveTo(tx,ty);ctx.lineTo(b.x,b.y);ctx.stroke();
    ctx.shadowColor=b.color;ctx.shadowBlur=12;ctx.fillStyle='#FFF';ctx.beginPath();ctx.arc(b.x,b.y,b.r*.72,0,TAU);ctx.fill();ctx.restore();
  }

  for(const e of enemies){
    if(e.dead)continue;
    glowDot(e.x,e.y+6,e.r*.7,'rgba(255,65,92,.30)',.35);
    drawGun(e.x,e.y,e.angle,'pistol',e.type==='heavy'?1.20:.96,true,e.type==='shield');
    if(e.hp<e.maxHp){
      const w=48;glassPanel(e.x-w/2,e.y-e.r-22,w,8,4,.82);
      fillRound(e.x-w/2+2,e.y-e.r-20,(w-4)*(e.hp/e.maxHp),4,2,COLORS.red);
    }
  }

  if(player){
    glowDot(player.x,player.y+6,player.r*.85,player.weapon==='shotgun'?'rgba(0,184,148,.28)':'rgba(108,92,231,.26)',.35);
    ctx.globalAlpha=player.invuln>0&&Math.floor(player.invuln*12)%2===0?.40:1;
    drawGun(player.x,player.y,player.angle,player.weapon,1.02);
    ctx.globalAlpha=1;
  }

  // Glass HUD.
  glassPanel(14,13,114,47,14,.90);
  for(let i=0;i<3;i++) text(i<player.hearts?'♥':'♡',32+i*31,37,24,i<player.hearts?COLORS.red:'#505A71');
  glassPanel(W/2-61,13,122,47,14,.90);text(`STAGE ${currentLevel}`,W/2,36,14,COLORS.text,'center',900);
  glassPanel(W-126,13,112,47,14,.90);text(`${enemies.filter(e=>!e.dead).length} LEFT`,W-70,36,13,COLORS.cyan,'center',900);

  if(currentLevel===1 && tutorialStep===0){
    glassPanel(W/2-122,a.y+a.h*.56-24,244,58,16,.72);
    text('TAP TO FIRE',W/2,a.y+a.h*.56-4,19,COLORS.white);
    text('RECOIL IS YOUR MOVEMENT',W/2,a.y+a.h*.56+18,10,COLORS.cyan,'center',800);
  } else if(currentLevel===1 && tutorialStep===1){
    text('USE THE KICK TO DODGE',W/2,a.y+a.h*.60,13,COLORS.cyan,'center',850);
  }
  if(LEVELS[currentLevel-1].boss){
    ctx.save();ctx.shadowColor='#D08BFF';ctx.shadowBlur=10;text('BOSS STAGE',W/2,a.y+28,12,'#E6B0FF');ctx.restore();
  }
  addButton('pause','Ⅱ',W-58,H-49,42,34,{accent:'#26314B'});drawButton(buttons[0]);
}

function drawVictory(){
  backgroundGrid();
  text('LEVEL COMPLETE',W/2,118,30,COLORS.green);text(`STAGE ${currentLevel}`,W/2,156,14,COLORS.muted);
  const stars=stateData.stars||2; text(`${'★'.repeat(stars)}${'☆'.repeat(3-stars)}`,W/2,220,44,COLORS.yellow);
  fillRound(28,278,W-56,126,20,COLORS.panel);text(`+${stateData.reward} COINS`,W/2,313,18,COLORS.yellow);text(`+${stateData.xp} XP`,W/2,347,16,COLORS.cyan);text(stars===3?'NO DAMAGE!':'KEEP MASTERING THE RECOIL',W/2,382,11,COLORS.muted);
  const bw=W-56; if(currentLevel<10) addButton('next','NEXT STAGE',28,455,bw,66,{accent:COLORS.purple});
  addButton('replay','REPLAY',28,currentLevel<10?538:455,bw,58,{accent:'#26304D'});addButton('home','HOME',28,currentLevel<10?610:528,bw,54,{accent:'#1B2238'});buttons.forEach(drawButton);
}
function drawDefeat(){
  backgroundGrid();text('DEFEAT',W/2,150,42,COLORS.red);text('THE RECOIL GOT YOU THIS TIME',W/2,198,12,COLORS.muted);
  drawGun(W/2,300,.55,save.selectedWeapon,1.35);const bw=W-56;addButton('replay','RETRY',28,420,bw,68,{accent:COLORS.red});addButton('home','HOME',28,507,bw,58,{accent:'#26304D'});buttons.forEach(drawButton);
}

function drawFx(){
  ctx.save();
  for(const p of particles){
    const a=clamp(p.life/p.max,0,1);ctx.globalAlpha=a;
    ctx.shadowColor=p.color;ctx.shadowBlur=8*p.size/4;ctx.fillStyle=p.color;
    ctx.beginPath();ctx.arc(p.x,p.y,p.size*(.55+.45*a),0,TAU);ctx.fill();
  }
  ctx.shadowBlur=0;ctx.globalAlpha=1;
  for(const f of floatingTexts){
    ctx.globalAlpha=clamp(f.life/f.max,0,1);
    ctx.save();ctx.shadowColor=f.color;ctx.shadowBlur=8;text(f.label,f.x,f.y,15,f.color);ctx.restore();
  }
  ctx.restore();ctx.globalAlpha=1;
}

function pointer(e){
  const r=canvas.getBoundingClientRect(); return {x:(e.clientX-r.left)*(W/r.width),y:(e.clientY-r.top)*(H/r.height)};
}
canvas.addEventListener('pointerdown',e=>{
  e.preventDefault();const p=pointer(e);
  if(state==='playing'){
    const b=hitButton(p.x,p.y); if(b&&b.id==='pause'){state='menu';sfx('click');return;}
    firePlayer();return;
  }
  const b=hitButton(p.x,p.y);if(!b)return;sfx('click');
  if(b.id==='play') state='levels';
  else if(b.id==='weapons') state='weapons';
  else if(b.id==='back') state='menu';
  else if(b.id.startsWith('level:')) startLevel(+b.id.split(':')[1]);
  else if(b.id.startsWith('weapon:')){save.selectedWeapon=b.id.split(':')[1];saveGame();}
  else if(b.id==='next') startLevel(Math.min(10,currentLevel+1));
  else if(b.id==='replay') startLevel(currentLevel);
  else if(b.id==='home') state='menu';
},{passive:false});

window.RecoilRivalsBack=()=>{
  if(state==='menu') return false;
  if(state==='playing'||state==='victory'||state==='defeat'||state==='levels'||state==='weapons'){state='menu';return true;}
  return false;
};

document.addEventListener('visibilitychange',()=>{ if(document.hidden && state==='playing') state='menu'; });

function loop(now){
  const dt=(now-last)/1000;last=now;update(dt);draw();requestAnimationFrame(loop);
}
requestAnimationFrame(loop);

// Tiny test hook used by the build checker.
window.__RR__={weapons,levels:LEVELS,enemyStats:ENEMY_STATS,defaultSave};
})();
