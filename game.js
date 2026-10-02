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
  const c=b.enabled?b.accent:'#33394C';
  ctx.save(); ctx.shadowColor=b.enabled?c:'transparent'; ctx.shadowBlur=b.enabled?16:0;
  fillRound(b.x,b.y,b.w,b.h,16,c); ctx.restore();
  fillRound(b.x+3,b.y+3,b.w-6,b.h-6,13,b.enabled?'#151A2B':'#1B1E28');
  text(b.label,b.x+b.w/2,b.y+b.h/2-(b.sub?7:0),Math.min(20,b.h*.34),b.enabled?COLORS.text:'#757B8D');
  if(b.sub) text(b.sub,b.x+b.w/2,b.y+b.h/2+16,11,b.enabled?COLORS.muted:'#555B6A','center',650);
}
function hitButton(x,y){ return buttons.find(b=>b.enabled&&x>=b.x&&x<=b.x+b.w&&y>=b.y&&y<=b.y+b.h); }

function drawGun(x,y,angle,weaponKey,scale=1,enemy=false,shield=false){
  const w = enemy ? {color:'#D94A5C',accent:'#FF8998'} : weapons[weaponKey];
  ctx.save(); ctx.translate(x,y); ctx.rotate(angle);
  ctx.shadowColor=enemy?'#FF5D73':w.accent; ctx.shadowBlur=10*scale;
  fillRound(-26*scale,-9*scale,48*scale,18*scale,6*scale,w.color);
  fillRound(10*scale,-5*scale,26*scale,10*scale,4*scale,w.accent);
  fillRound(-6*scale,6*scale,13*scale,25*scale,4*scale,enemy?'#8F3040':w.color);
  ctx.shadowBlur=0;
  ctx.fillStyle='#0C1020'; ctx.beginPath();ctx.arc(-11*scale,0,4*scale,0,TAU);ctx.fill();
  if(shield){
    ctx.strokeStyle='#74B9FF';ctx.lineWidth=6*scale;ctx.beginPath();ctx.arc(0,0,32*scale,-1.25,1.25);ctx.stroke();
  }
  ctx.restore();
}

function startLevel(n){
  currentLevel=clamp(n,1,LEVELS.length); const cfg=LEVELS[currentLevel-1];
  state='playing'; buttons=[]; bullets=[]; particles=[]; floatingTexts=[]; obstacles=[]; finishTimer=0; timeScale=1; tutorialStep=0;
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
  const g=ctx.createLinearGradient(0,0,0,H);g.addColorStop(0,'#101526');g.addColorStop(1,'#070910');ctx.fillStyle=g;ctx.fillRect(0,0,W,H);
  ctx.strokeStyle='rgba(120,135,180,.065)';ctx.lineWidth=1;const s=36;for(let x=0;x<W;x+=s){ctx.beginPath();ctx.moveTo(x,0);ctx.lineTo(x,H);ctx.stroke();}for(let y=0;y<H;y+=s){ctx.beginPath();ctx.moveTo(0,y);ctx.lineTo(W,y);ctx.stroke();}
}
function drawTopBar(title){
  fillRound(12,12,W-24,52,17,'rgba(17,21,36,.92)');strokeRound(12,12,W-24,52,17,'rgba(255,255,255,.06)');
  text(title,W/2,38,16,COLORS.text); text('◆ '+save.coins,28,38,15,COLORS.yellow,'left',850);
}

function drawMenu(){
  backgroundGrid(); drawTopBar('LV '+save.playerLevel);
  text('RECOIL',W/2,118,39,COLORS.text,'center',950);text('RIVALS',W/2,154,39,COLORS.cyan,'center',950);
  text('SHOOT • RECOIL • SURVIVE',W/2,186,12,COLORS.muted,'center',750);
  ctx.save();ctx.translate(W/2,H*.39);ctx.rotate(menuGunAngle);ctx.shadowColor=weapons[save.selectedWeapon].color;ctx.shadowBlur=28;drawGun(0,0,0,save.selectedWeapon,1.65);ctx.restore();
  text(weapons[save.selectedWeapon].name,W/2,H*.51,15,weapons[save.selectedWeapon].accent);
  const bw=Math.min(330,W-44),x=(W-bw)/2;
  addButton('play','PLAY',x,H*.60,bw,68,{accent:COLORS.purple});
  addButton('weapons','WEAPONS',x,H*.70,bw,58,{accent:'#26304D'});
  text(`STAGE ${Math.min(save.levelUnlocked,10)} / 10 UNLOCKED`,W/2,H*.81,12,COLORS.muted);
  text('Prototype v0.1',W/2,H-24,10,'#5E6680','center',650);
  buttons.forEach(drawButton);
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
  const g=ctx.createLinearGradient(a.x,a.y,a.x,a.y+a.h);g.addColorStop(0,'#11172A');g.addColorStop(1,'#0A0D18');fillRound(a.x,a.y,a.w,a.h,22,g);
  ctx.save();roundRectPath(a.x,a.y,a.w,a.h,22);ctx.clip();
  ctx.strokeStyle='rgba(124,139,190,.07)';ctx.lineWidth=1;for(let y=a.y+20;y<a.y+a.h;y+=42){ctx.beginPath();ctx.moveTo(a.x,y);ctx.lineTo(a.x+a.w,y);ctx.stroke();}
  ctx.restore();strokeRound(a.x,a.y,a.w,a.h,22,'#2A3352',2);
  for(const o of obstacles){fillRound(o.x,o.y,o.w,o.h,6,COLORS.wall);ctx.fillStyle=COLORS.wallEdge;ctx.fillRect(o.x+4,o.y+3,Math.max(0,o.w-8),3);}
}
function drawGame(){
  backgroundGrid();drawArena();const a=arenaRect();
  for(const b of bullets){ctx.save();ctx.shadowColor=b.color;ctx.shadowBlur=10;ctx.fillStyle=b.color;ctx.beginPath();ctx.arc(b.x,b.y,b.r,0,TAU);ctx.fill();ctx.restore();}
  for(const e of enemies){if(e.dead)continue;drawGun(e.x,e.y,e.angle,'pistol',e.type==='heavy'?1.18:.94,true,e.type==='shield');if(e.hp<e.maxHp){const w=44;fillRound(e.x-w/2,e.y-e.r-19,w,5,3,'#3A2630');fillRound(e.x-w/2,e.y-e.r-19,w*(e.hp/e.maxHp),5,3,COLORS.red);}}
  if(player){ctx.globalAlpha=player.invuln>0&&Math.floor(player.invuln*12)%2===0?.35:1;drawGun(player.x,player.y,player.angle,player.weapon,1.0);ctx.globalAlpha=1;}
  fillRound(16,14,114,44,14,'rgba(8,10,18,.86)');for(let i=0;i<3;i++) text(i<player.hearts?'♥':'♡',34+i*31,37,25,i<player.hearts?COLORS.red:'#50566B');
  fillRound(W/2-60,14,120,44,14,'rgba(8,10,18,.86)');text(`STAGE ${currentLevel}`,W/2,37,15,COLORS.text);
  fillRound(W-124,14,108,44,14,'rgba(8,10,18,.86)');text(`${enemies.filter(e=>!e.dead).length} LEFT`,W-70,37,14,COLORS.text);
  if(currentLevel===1 && tutorialStep===0){text('TAP TO SHOOT',W/2,a.y+a.h*.57,21,COLORS.white);text('YOUR SHOT PUSHES YOU BACK',W/2,a.y+a.h*.62,11,COLORS.cyan);}
  else if(currentLevel===1 && tutorialStep===1){text('USE RECOIL TO MOVE',W/2,a.y+a.h*.58,16,COLORS.cyan);}
  if(LEVELS[currentLevel-1].boss) text('BOSS STAGE',W/2,a.y+30,12,'#E6B0FF');
  addButton('pause','Ⅱ',W-58,H-50,42,34,{accent:'#222A43'});drawButton(buttons[0]);
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
  for(const p of particles){ctx.globalAlpha=clamp(p.life/p.max,0,1);ctx.fillStyle=p.color;ctx.beginPath();ctx.arc(p.x,p.y,p.size,0,TAU);ctx.fill();}
  ctx.globalAlpha=1;
  for(const f of floatingTexts){ctx.globalAlpha=clamp(f.life/f.max,0,1);text(f.label,f.x,f.y,15,f.color);}
  ctx.globalAlpha=1;
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
