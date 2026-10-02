using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace RecoilRivals2
{
    public class RRGameV2 : MonoBehaviour
    {
        enum ScreenMode { Menu, Loadout, Playing, Result }
        enum WeaponType { Pistol, Revolver, Shotgun }
        enum EnemyType { Standard, Fast, Heavy, Shotgun }

        class Enemy
        {
            public RectTransform root;
            public Image hpFill;
            public Vector2 pos;
            public float angle;
            public float hp;
            public float maxHp;
            public float fireTimer;
            public EnemyType type;
        }

        class Bullet
        {
            public RectTransform root;
            public Vector2 pos;
            public Vector2 vel;
            public float life;
            public float damage;
            public bool playerOwned;
            public int bounces;
        }

        class Fx
        {
            public RectTransform root;
            public float life;
            public float maxLife;
            public Vector2 vel;
            public float grow;
        }

        struct WeaponStats
        {
            public string name;
            public float damage, cooldown, recoil, spin, speed, spread;
            public int pellets, bounces;
            public WeaponStats(string n,float d,float c,float r,float s,float sp,int p,float spread,int b)
            { name=n;damage=d;cooldown=c;recoil=r;spin=s;speed=sp;pellets=p;this.spread=spread;bounces=b; }
        }

        Canvas canvas;
        CanvasScaler scaler;
        RectTransform safeRoot;
        RectTransform worldLayer;
        RectTransform overlayLayer;
        RectTransform arena;
        RectTransform arenaContent;
        RectTransform playerRoot;
        RectTransform playerModel;
        RectTransform aimRing;
        Text heartsText;
        Text levelText;
        Text enemiesText;
        Text coinText;
        Font font;
        Sprite rounded;
        Sprite circle;

        readonly List<Enemy> enemies = new List<Enemy>();
        readonly List<Bullet> bullets = new List<Bullet>();
        readonly List<Fx> effects = new List<Fx>();
        readonly List<Rect> obstacles = new List<Rect>();

        WeaponStats[] weapons;
        ScreenMode mode;
        WeaponType selectedWeapon;
        Vector2 playerPos;
        Vector2 playerVel;
        float playerAngle;
        float playerAngularVel;
        float nextPlayerShot;
        int playerHp = 3;
        int level;
        int coins;
        bool resolving;
        float arenaShake;
        float arenaShakePower;

        readonly Color bg = new Color(0.020f,0.025f,0.035f,1f);
        readonly Color panel = new Color(0.035f,0.045f,0.060f,1f);
        readonly Color panel2 = new Color(0.050f,0.065f,0.083f,1f);
        readonly Color cyan = new Color(0.12f,0.93f,0.80f,1f);
        readonly Color cyanSoft = new Color(0.25f,0.68f,0.68f,1f);
        readonly Color orange = new Color(1.0f,0.37f,0.10f,1f);
        readonly Color red = new Color(1.0f,0.22f,0.18f,1f);
        readonly Color white = new Color(0.92f,0.96f,0.98f,1f);
        readonly Color muted = new Color(0.44f,0.54f,0.60f,1f);

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 0;
            Screen.orientation = ScreenOrientation.Portrait;

            level = Mathf.Clamp(PlayerPrefs.GetInt("RR2_Level", 1), 1, 10);
            coins = Mathf.Max(0, PlayerPrefs.GetInt("RR2_Coins", 0));
            selectedWeapon = (WeaponType)Mathf.Clamp(PlayerPrefs.GetInt("RR2_Weapon", 0), 0, 2);

            weapons = new[]
            {
                new WeaponStats("VANTA PISTOL",1f,.25f,.42f,92f,1.55f,1,0f,0),
                new WeaponStats("IRON REVOLVER",2f,.48f,.62f,138f,1.72f,1,0f,1),
                new WeaponStats("BREACH SHOTGUN",.68f,.68f,.78f,170f,1.38f,5,12f,0)
            };

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            rounded = MakeRoundedSprite(96,22f);
            circle = MakeRoundedSprite(64,31f);

            BuildCanvas();
            ShowMenu();
        }

        void BuildCanvas()
        {
            var canvasGO = new GameObject("RR2 Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);
            canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2400);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .55f;

            safeRoot = NewRect("Safe Area", canvasGO.transform);
            Stretch(safeRoot, Vector2.zero, Vector2.one);
            ApplySafeArea();

            worldLayer = NewRect("World Layer", safeRoot);
            Stretch(worldLayer, Vector2.zero, Vector2.one);

            overlayLayer = NewRect("Overlay Layer", safeRoot);
            Stretch(overlayLayer, Vector2.zero, Vector2.one);

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("RR2 EventSystem", typeof(EventSystem));
                es.transform.SetParent(transform, false);
                var module = es.AddComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
            }
        }

        void ApplySafeArea()
        {
            Rect a = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            safeRoot.anchorMin = new Vector2(a.xMin / Screen.width, a.yMin / Screen.height);
            safeRoot.anchorMax = new Vector2(a.xMax / Screen.width, a.yMax / Screen.height);
            safeRoot.offsetMin = Vector2.zero;
            safeRoot.offsetMax = Vector2.zero;
        }

        void Update()
        {
            if (mode == ScreenMode.Playing)
            {
                float dt = Mathf.Min(Time.deltaTime, .035f);
                if (!resolving && FirePressed()) FirePlayer();
                UpdatePlayer(dt);
                UpdateEnemies(dt);
                UpdateBullets(dt);
                UpdateEffects(dt);
                UpdateHUD();
                UpdateShake(dt);
            }
        }

        bool FirePressed()
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) return true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
            return false;
        }

        void ShowMenu()
        {
            mode = ScreenMode.Menu;
            resolving = false;
            ClearLayers();

            AddImage(worldLayer, "BG", Vector2.zero, Vector2.one, bg, null);
            AddImage(worldLayer, "Top Glow", new Vector2(0,.76f), new Vector2(1,1), new Color(.02f,.18f,.18f,.32f), null);
            AddDecorativeRails(worldLayer);

            AddText(worldLayer, "RECOIL", 105, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(.06f,.80f), new Vector2(.94f,.90f), white);
            AddText(worldLayer, "RIVALS", 105, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(.06f,.735f), new Vector2(.94f,.835f), cyan);
            AddText(worldLayer, "TACTICAL RECOIL DUELS", 24, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(.10f,.69f), new Vector2(.90f,.73f), muted);

            AddChip(worldLayer, "LV "+level, new Vector2(.055f,.925f), new Vector2(.30f,.975f), cyan);
            AddChip(worldLayer, "◈ "+coins, new Vector2(.70f,.925f), new Vector2(.945f,.975f), orange);

            var weaponCard = AddPanel(worldLayer, "Weapon Card", new Vector2(.08f,.36f), new Vector2(.92f,.64f), new Color(.025f,.033f,.045f,.98f), rounded);
            AddImage(weaponCard, "Card Top", new Vector2(0,.86f), new Vector2(1,1), new Color(.055f,.075f,.09f,1f), null);
            AddText(weaponCard, "ACTIVE LOADOUT", 22, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(.06f,.87f), new Vector2(.50f,.98f), muted);
            AddText(weaponCard, weapons[(int)selectedWeapon].name, 36, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(.06f,.08f), new Vector2(.66f,.26f), white);
            BuildWeaponGraphic(weaponCard, new Vector2(.52f,.29f), new Vector2(.94f,.83f), cyan, white, selectedWeapon, false);

            AddButton(worldLayer, "PLAY  /  LEVEL "+level, new Vector2(.09f,.19f), new Vector2(.91f,.285f), cyan, new Color(.01f,.06f,.055f), () => StartLevel(level), 40);
            AddButton(worldLayer, "LOADOUT", new Vector2(.20f,.105f), new Vector2(.80f,.165f), panel2, white, ShowLoadout, 27);

            AddText(worldLayer, "TAP TO FIRE  •  RECOIL TO MOVE", 22, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(.05f,.035f), new Vector2(.95f,.075f), new Color(.30f,.45f,.50f,1f));
        }

        void ShowLoadout()
        {
            mode = ScreenMode.Loadout;
            ClearLayers();
            AddImage(worldLayer, "BG", Vector2.zero, Vector2.one, bg, null);
            AddDecorativeRails(worldLayer);
            AddText(worldLayer, "LOADOUT", 68, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(.07f,.86f), new Vector2(.93f,.95f), white);
            AddText(worldLayer, "Choose your recoil profile.", 25, FontStyle.Normal, TextAnchor.MiddleLeft, new Vector2(.07f,.81f), new Vector2(.93f,.86f), muted);

            for (int i=0;i<3;i++)
            {
                int idx=i;
                float y=.61f-i*.205f;
                var card=AddPanel(worldLayer,"Weapon "+i,new Vector2(.065f,y),new Vector2(.935f,y+.165f),i==(int)selectedWeapon?new Color(.035f,.13f,.125f,1f):panel,rounded);
                BuildWeaponGraphic(card,new Vector2(.04f,.15f),new Vector2(.35f,.85f),i==(int)selectedWeapon?cyan:cyanSoft,white,(WeaponType)i,false);
                AddText(card,weapons[i].name,31,FontStyle.Bold,TextAnchor.MiddleLeft,new Vector2(.38f,.52f),new Vector2(.72f,.86f),white);
                AddText(card,"DMG "+weapons[i].damage.ToString("0.0")+"   RECOIL "+Mathf.RoundToInt(weapons[i].recoil*100),19,FontStyle.Bold,TextAnchor.MiddleLeft,new Vector2(.38f,.22f),new Vector2(.74f,.52f),muted);
                AddButton(card,i==(int)selectedWeapon?"EQUIPPED":"EQUIP",new Vector2(.74f,.24f),new Vector2(.95f,.76f),i==(int)selectedWeapon?cyan:panel2,i==(int)selectedWeapon?bg:white,()=>{selectedWeapon=(WeaponType)idx;PlayerPrefs.SetInt("RR2_Weapon",idx);PlayerPrefs.Save();ShowLoadout();},19);
            }
            AddButton(worldLayer,"BACK",new Vector2(.22f,.07f),new Vector2(.78f,.13f),panel2,white,ShowMenu,27);
        }

        void StartLevel(int target)
        {
            mode = ScreenMode.Playing;
            resolving = false;
            level = Mathf.Clamp(target,1,10);
            playerHp = 3;
            bullets.Clear();
            enemies.Clear();
            effects.Clear();
            obstacles.Clear();
            ClearLayers();

            AddImage(worldLayer, "Game BG", Vector2.zero, Vector2.one, bg, null);

            AddText(worldLayer, "RECOIL RIVALS", 20, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(.045f,.94f), new Vector2(.40f,.985f), muted);
            heartsText = AddText(worldLayer, "♥  ♥  ♥", 32, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(.045f,.885f), new Vector2(.35f,.94f), red);
            levelText = AddText(worldLayer, "LEVEL "+level, 30, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(.34f,.885f), new Vector2(.66f,.94f), white);
            enemiesText = AddText(worldLayer, "TARGETS 0", 22, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(.62f,.885f), new Vector2(.955f,.94f), orange);
            coinText = AddText(worldLayer, "◈ "+coins, 20, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(.70f,.94f), new Vector2(.955f,.985f), muted);

            arena = AddPanel(worldLayer,"ARENA",new Vector2(.045f,.115f),new Vector2(.955f,.87f),new Color(.027f,.035f,.048f,1f),rounded);
            var frame=arena.gameObject.AddComponent<Outline>();
            frame.effectColor=new Color(.10f,.34f,.36f,.9f);
            frame.effectDistance=new Vector2(3f,-3f);

            arenaContent=NewRect("Arena Content",arena);
            Stretch(arenaContent,new Vector2(.02f,.02f),new Vector2(.98f,.98f));
            arenaContent.gameObject.AddComponent<RectMask2D>();

            AddArenaDetail();
            BuildObstaclesForLevel(level);

            playerPos = new Vector2(-.42f,-.34f);
            playerVel = Vector2.zero;
            playerAngle = 14f;
            playerAngularVel = 0f;
            playerRoot = NewRect("PLAYER", arenaContent);
            SetArenaTransform(playerRoot, playerPos, new Vector2(170,80), playerAngle);
            BuildWeaponGraphic(playerRoot,Vector2.zero,Vector2.one,cyan,white,selectedWeapon,true);

            aimRing = AddImage(arenaContent,"Player Ring",Vector2.zero,Vector2.zero,new Color(.1f,.95f,.80f,.12f),circle);
            aimRing.sizeDelta=new Vector2(210,210);
            aimRing.anchorMin=aimRing.anchorMax=new Vector2(.5f,.5f);
            aimRing.pivot=new Vector2(.5f,.5f);

            SpawnEnemies(level);
            AddText(worldLayer,level<=3 ? "TAP ANYWHERE TO FIRE  •  SHOTS MOVE YOU" : "TAP TO FIRE", 20, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(.07f,.045f), new Vector2(.93f,.095f), level<=3?cyanSoft:muted);
            UpdateHUD();
        }

        void AddArenaDetail()
        {
            for(int i=1;i<6;i++)
            {
                float y=.12f+i*.145f;
                AddImage(arenaContent,"Horizontal Rail",new Vector2(.03f,y),new Vector2(.97f,y+.003f),new Color(.10f,.18f,.21f,.42f),null);
            }
            for(int i=1;i<4;i++)
            {
                float x=.08f+i*.21f;
                AddImage(arenaContent,"Vertical Rail",new Vector2(x,.03f),new Vector2(x+.003f,.97f),new Color(.08f,.14f,.17f,.25f),null);
            }

            var tag=AddPanel(arenaContent,"Sector",new Vector2(.035f,.91f),new Vector2(.27f,.965f),new Color(.06f,.085f,.105f,.95f),rounded);
            AddText(tag,"SECTOR 0"+Mathf.Clamp(level,1,9),16,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.05f,.1f),new Vector2(.95f,.9f),cyanSoft);
        }

        void BuildObstaclesForLevel(int l)
        {
            if(l>=2) AddObstacle(new Rect(-.05f,-.02f,.48f,.055f), -9f);
            if(l>=4) AddObstacle(new Rect(-.63f,.21f,.34f,.05f), 12f);
            if(l>=6) AddObstacle(new Rect(.30f,-.30f,.34f,.05f), -14f);
            if(l>=8) AddObstacle(new Rect(-.18f,.40f,.42f,.05f), 6f);
            if(l==10) AddObstacle(new Rect(-.34f,-.47f,.30f,.05f),18f);
        }

        void AddObstacle(Rect r,float angle)
        {
            obstacles.Add(r);
            var o=AddPanel(arenaContent,"Barrier",Vector2.zero,Vector2.zero,panel2,rounded);
            o.anchorMin=o.anchorMax=new Vector2(.5f,.5f);
            o.pivot=new Vector2(.5f,.5f);
            o.sizeDelta=new Vector2(r.width*ArenaPixelSize().x,r.height*ArenaPixelSize().y);
            o.anchoredPosition=new Vector2(r.center.x*ArenaPixelSize().x*.5f,r.center.y*ArenaPixelSize().y*.5f);
            o.localRotation=Quaternion.Euler(0,0,angle);
            var stripe=AddImage(o,"Energy Edge",new Vector2(.02f,.72f),new Vector2(.98f,.96f),new Color(.12f,.82f,.72f,.78f),rounded);
            stripe.GetComponent<Image>().raycastTarget=false;
        }

        void SpawnEnemies(int l)
        {
            if(l==10)
            {
                AddEnemy(new Vector2(.44f,.42f),EnemyType.Heavy,7f);
                AddEnemy(new Vector2(-.42f,.18f),EnemyType.Fast,2f);
                AddEnemy(new Vector2(.34f,-.05f),EnemyType.Shotgun,3f);
                return;
            }

            int count=Mathf.Clamp(1+(l-1)/2,1,4);
            Vector2[] spots={new Vector2(.43f,.39f),new Vector2(-.42f,.19f),new Vector2(.39f,-.08f),new Vector2(-.34f,.47f)};
            for(int i=0;i<count;i++)
            {
                EnemyType type=(EnemyType)((l+i-1)%4);
                float hp=l<=2?1f:(type==EnemyType.Heavy?4f:type==EnemyType.Shotgun?3f:2f);
                AddEnemy(spots[i],type,hp);
            }
        }

        void AddEnemy(Vector2 pos,EnemyType type,float hp)
        {
            var e=new Enemy { pos=pos,type=type,hp=hp,maxHp=hp,angle=180f };
            e.root=NewRect(type+" Enemy",arenaContent);
            SetArenaTransform(e.root,pos,type==EnemyType.Heavy?new Vector2(190,88):new Vector2(155,72),e.angle);
            Color main=type==EnemyType.Fast?new Color(1f,.45f,.08f):type==EnemyType.Heavy?new Color(.68f,.18f,.92f):type==EnemyType.Shotgun?new Color(1f,.12f,.45f):new Color(.95f,.24f,.18f);
            BuildWeaponGraphic(e.root,Vector2.zero,Vector2.one,main,new Color(1f,.72f,.45f),WeaponType.Pistol,true);

            var hpBg=AddPanel(e.root,"HP BG",new Vector2(.18f,1.06f),new Vector2(.82f,1.15f),new Color(.08f,.09f,.11f,.95f),circle);
            e.hpFill=AddImage(hpBg,"HP",new Vector2(.03f,.18f),new Vector2(.97f,.82f),main,circle).GetComponent<Image>();
            e.fireTimer=.9f+(int)type*.18f+enemies.Count*.15f;
            enemies.Add(e);
        }

        void UpdatePlayer(float dt)
        {
            playerVel.y -= .12f*dt;
            playerVel *= Mathf.Pow(.46f,dt);
            playerPos += playerVel*dt;
            playerAngle += playerAngularVel*dt;
            playerAngularVel *= Mathf.Pow(.25f,dt);

            float boundX=.82f,boundY=.84f;
            if(playerPos.x<-boundX){playerPos.x=-boundX;playerVel.x=Mathf.Abs(playerVel.x)*.60f;}
            if(playerPos.x> boundX){playerPos.x= boundX;playerVel.x=-Mathf.Abs(playerVel.x)*.60f;}
            if(playerPos.y<-boundY){playerPos.y=-boundY;playerVel.y=Mathf.Abs(playerVel.y)*.58f;}
            if(playerPos.y> boundY){playerPos.y= boundY;playerVel.y=-Mathf.Abs(playerVel.y)*.58f;}

            SetArenaTransform(playerRoot,playerPos,new Vector2(170,80),playerAngle);
            if(aimRing!=null)
            {
                aimRing.anchoredPosition=ToArenaPixels(playerPos);
                aimRing.localScale=Vector3.one*(1f+Mathf.Sin(Time.time*3.4f)*.035f);
            }
        }

        void FirePlayer()
        {
            if(Time.time<nextPlayerShot) return;
            WeaponStats s=weapons[(int)selectedWeapon];
            nextPlayerShot=Time.time+s.cooldown;
            Vector2 dir=AngleDir(playerAngle);
            Vector2 muzzle=playerPos+dir*.105f;

            int pellets=Mathf.Max(1,s.pellets);
            for(int i=0;i<pellets;i++)
            {
                float spread=pellets==1?0f:Mathf.Lerp(-s.spread,s.spread,i/(float)(pellets-1));
                SpawnBullet(muzzle,Rotate(dir,spread)*s.speed,true,s.damage,s.bounces,cyan);
            }

            playerVel -= dir*s.recoil;
            playerAngularVel += s.spin;
            SpawnBurst(muzzle,cyan,6);
            arenaShake=.11f; arenaShakePower=8f;
        }

        void UpdateEnemies(float dt)
        {
            for(int i=enemies.Count-1;i>=0;i--)
            {
                Enemy e=enemies[i];
                Vector2 delta=playerPos-e.pos;
                float target=Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg;
                float turn=e.type==EnemyType.Fast?260f:150f;
                e.angle=Mathf.MoveTowardsAngle(e.angle,target,turn*dt);
                e.fireTimer-=dt;

                if(!resolving && e.fireTimer<=0f)
                {
                    Vector2 dir=AngleDir(e.angle);
                    float interval=1.45f,speed=1.05f,damage=1f;
                    if(e.type==EnemyType.Fast){interval=.78f;speed=1.28f;}
                    if(e.type==EnemyType.Heavy){interval=2.0f;speed=.88f;damage=1.25f;}
                    if(e.type==EnemyType.Shotgun){interval=1.65f;speed=1.0f;}
                    e.fireTimer=interval;

                    if(e.type==EnemyType.Shotgun)
                        for(int p=0;p<4;p++) SpawnBullet(e.pos+dir*.10f,Rotate(dir,Mathf.Lerp(-11f,11f,p/3f))*speed,false,1f,0,orange);
                    else SpawnBullet(e.pos+dir*.10f,dir*speed,false,damage,0,e.type==EnemyType.Heavy?new Color(.86f,.36f,1f):orange);

                    SpawnBurst(e.pos+dir*.10f,orange,4);
                }

                SetArenaTransform(e.root,e.pos,e.type==EnemyType.Heavy?new Vector2(190,88):new Vector2(155,72),e.angle);
            }
        }

        void SpawnBullet(Vector2 pos,Vector2 vel,bool playerOwned,float damage,int bounces,Color color)
        {
            var root=AddImage(arenaContent,playerOwned?"Player Bullet":"Enemy Bullet",Vector2.zero,Vector2.zero,color,circle);
            root.anchorMin=root.anchorMax=new Vector2(.5f,.5f);
            root.pivot=new Vector2(.5f,.5f);
            root.sizeDelta=playerOwned?new Vector2(20,20):new Vector2(23,23);
            root.anchoredPosition=ToArenaPixels(pos);

            var trail=AddImage(arenaContent,"Trail",Vector2.zero,Vector2.zero,new Color(color.r,color.g,color.b,.22f),circle);
            trail.anchorMin=trail.anchorMax=new Vector2(.5f,.5f);
            trail.pivot=new Vector2(.5f,.5f);
            trail.sizeDelta=new Vector2(10,55);
            trail.anchoredPosition=root.anchoredPosition;
            trail.SetAsFirstSibling();

            var b=new Bullet{root=root,pos=pos,vel=vel,life=3.8f,damage=damage,playerOwned=playerOwned,bounces=bounces};
            bullets.Add(b);
        }

        void UpdateBullets(float dt)
        {
            for(int i=bullets.Count-1;i>=0;i--)
            {
                Bullet b=bullets[i];
                b.life-=dt;
                b.pos+=b.vel*dt;

                bool remove=b.life<=0f;
                if(Mathf.Abs(b.pos.x)>.93f)
                {
                    if(b.bounces>0){b.pos.x=Mathf.Sign(b.pos.x)*.93f;b.vel.x*=-1;b.bounces--;}
                    else remove=true;
                }
                if(Mathf.Abs(b.pos.y)>.94f)
                {
                    if(b.bounces>0){b.pos.y=Mathf.Sign(b.pos.y)*.94f;b.vel.y*=-1;b.bounces--;}
                    else remove=true;
                }

                if(!remove && b.playerOwned)
                {
                    for(int e=enemies.Count-1;e>=0;e--)
                    {
                        if((enemies[e].pos-b.pos).sqrMagnitude<.0135f)
                        {
                            DamageEnemy(enemies[e],b.damage);
                            remove=true;
                            break;
                        }
                    }
                }
                else if(!remove && !b.playerOwned && (playerPos-b.pos).sqrMagnitude<.014f)
                {
                    DamagePlayer(b.damage);
                    remove=true;
                }

                if(remove)
                {
                    if(b.root!=null) Destroy(b.root.gameObject);
                    bullets.RemoveAt(i);
                }
                else if(b.root!=null) b.root.anchoredPosition=ToArenaPixels(b.pos);
            }
        }

        void DamageEnemy(Enemy e,float damage)
        {
            e.hp-=damage;
            e.hpFill.fillAmount=Mathf.Clamp01(e.hp/e.maxHp);
            SpawnBurst(e.pos,orange,7);
            arenaShake=.10f;arenaShakePower=7f;
            if(e.hp<=0f)
            {
                coins+=8;
                if(e.root!=null) Destroy(e.root.gameObject);
                enemies.Remove(e);
                if(enemies.Count==0 && !resolving)
                {
                    resolving=true;
                    Invoke(nameof(WinLevel),.42f);
                }
            }
        }

        void DamagePlayer(float damage)
        {
            if(resolving) return;
            playerHp-=Mathf.CeilToInt(damage);
            playerHp=Mathf.Max(0,playerHp);
            SpawnBurst(playerPos,cyan,8);
            arenaShake=.16f;arenaShakePower=13f;
#if UNITY_ANDROID && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
            if(playerHp<=0)
            {
                resolving=true;
                Invoke(nameof(LoseLevel),.35f);
            }
        }

        void WinLevel()
        {
            int completed=level;
            int reward=40+completed*8;
            coins+=reward;
            if(completed<10) level=completed+1;
            PlayerPrefs.SetInt("RR2_Level",level);
            PlayerPrefs.SetInt("RR2_Coins",coins);
            PlayerPrefs.Save();
            ShowResult(true,reward,completed);
        }

        void LoseLevel(){ ShowResult(false,0,level); }

        void ShowResult(bool victory,int reward,int completed)
        {
            mode=ScreenMode.Result;
            var dim=AddPanel(overlayLayer,"Result",Vector2.zero,Vector2.one,new Color(.01f,.014f,.02f,.94f),null);
            AddText(dim,victory?"LEVEL CLEARED":"WEAPON DOWN",64,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.06f,.67f),new Vector2(.94f,.79f),victory?cyan:red);
            AddText(dim,victory?("+"+reward+"  COINS"):"CONTROL THE RECOIL",28,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.08f,.59f),new Vector2(.92f,.66f),white);

            if(victory && completed<10)
                AddButton(dim,"NEXT  /  LEVEL "+(completed+1),new Vector2(.12f,.40f),new Vector2(.88f,.49f),cyan,bg,()=>StartLevel(completed+1),34);
            else
                AddButton(dim,victory?"REPLAY":"RETRY",new Vector2(.12f,.40f),new Vector2(.88f,.49f),victory?cyan:red,victory?bg:white,()=>StartLevel(completed),34);

            AddButton(dim,"HOME",new Vector2(.22f,.29f),new Vector2(.78f,.36f),panel2,white,ShowMenu,26);
        }

        void UpdateHUD()
        {
            if(heartsText!=null) heartsText.text=playerHp>=3?"♥  ♥  ♥":playerHp==2?"♥  ♥":playerHp==1?"♥":"—";
            if(levelText!=null) levelText.text="LEVEL "+level;
            if(enemiesText!=null) enemiesText.text="TARGETS "+enemies.Count;
            if(coinText!=null) coinText.text="◈ "+coins;
        }

        void SpawnBurst(Vector2 pos,Color color,int count)
        {
            for(int i=0;i<count;i++)
            {
                float a=(360f/count)*i+(i%2)*13f;
                var p=AddImage(arenaContent,"Spark",Vector2.zero,Vector2.zero,color,circle);
                p.anchorMin=p.anchorMax=new Vector2(.5f,.5f);
                p.pivot=new Vector2(.5f,.5f);
                p.sizeDelta=new Vector2(12,12);
                p.anchoredPosition=ToArenaPixels(pos);
                effects.Add(new Fx{root=p,life=.28f+(i%3)*.05f,maxLife=.38f,vel=AngleDir(a)*(.25f+(i%3)*.08f),grow=-.75f});
            }
        }

        void UpdateEffects(float dt)
        {
            for(int i=effects.Count-1;i>=0;i--)
            {
                Fx f=effects[i];
                f.life-=dt;
                if(f.root!=null)
                {
                    Vector2 p=FromArenaPixels(f.root.anchoredPosition);
                    p+=f.vel*dt;
                    f.root.anchoredPosition=ToArenaPixels(p);
                    float s=Mathf.Clamp01(f.life/f.maxLife);
                    f.root.localScale=Vector3.one*s;
                }
                if(f.life<=0f)
                {
                    if(f.root!=null)Destroy(f.root.gameObject);
                    effects.RemoveAt(i);
                }
            }
        }

        void UpdateShake(float dt)
        {
            if(arena==null)return;
            if(arenaShake>0f)
            {
                arenaShake-=dt;
                arena.anchoredPosition=new Vector2(Mathf.Sin(Time.time*83f),Mathf.Cos(Time.time*97f))*arenaShakePower;
                arenaShakePower*=.90f;
            }
            else arena.anchoredPosition=Vector2.zero;
        }

        void AddDecorativeRails(RectTransform parent)
        {
            for(int i=0;i<5;i++)
            {
                float x=.08f+i*.21f;
                AddImage(parent,"Rail",new Vector2(x,.12f),new Vector2(x+.0025f,.67f),new Color(.09f,.23f,.26f,.38f),null);
            }
            AddImage(parent,"Accent Line",new Vector2(.08f,.335f),new Vector2(.92f,.338f),new Color(.1f,.70f,.62f,.35f),null);
        }

        void BuildWeaponGraphic(RectTransform parent,Vector2 min,Vector2 max,Color main,Color accent,WeaponType type,bool compact)
        {
            var root=NewRect("Weapon Visual",parent);
            Stretch(root,min,max);
            root.pivot=new Vector2(.5f,.5f);

            AddImage(root,"Body",new Vector2(.18f,.38f),new Vector2(.72f,.64f),main,rounded);
            AddImage(root,"Slide",new Vector2(.30f,.48f),new Vector2(.78f,.67f),new Color(main.r*.72f,main.g*.72f,main.b*.72f,1f),rounded);
            AddImage(root,"Barrel",new Vector2(.69f,.44f),new Vector2(.92f,.58f),accent,rounded);
            var grip=AddImage(root,"Grip",new Vector2(.26f,.08f),new Vector2(.42f,.43f),new Color(main.r*.72f,main.g*.72f,main.b*.72f,1f),rounded);
            grip.localRotation=Quaternion.Euler(0,0,-12f);
            AddImage(root,"Sight",new Vector2(.48f,.66f),new Vector2(.58f,.73f),accent,rounded);
            AddImage(root,"Muzzle",new Vector2(.88f,.40f),new Vector2(.96f,.62f),main,rounded);

            if(type==WeaponType.Revolver)
            {
                AddImage(root,"Cylinder",new Vector2(.42f,.31f),new Vector2(.60f,.60f),accent,circle);
                AddImage(root,"Long Barrel",new Vector2(.58f,.45f),new Vector2(.94f,.57f),main,rounded);
            }
            else if(type==WeaponType.Shotgun)
            {
                AddImage(root,"Heavy Barrel",new Vector2(.55f,.41f),new Vector2(.95f,.60f),main,rounded);
                AddImage(root,"Pump",new Vector2(.56f,.26f),new Vector2(.74f,.42f),accent,rounded);
                grip.anchorMin=new Vector2(.22f,.05f);grip.anchorMax=new Vector2(.39f,.43f);
            }
        }

        Vector2 ArenaPixelSize()
        {
            if(arenaContent==null)return new Vector2(900,1500);
            Vector2 s=arenaContent.rect.size;
            if(s.x<10||s.y<10){Canvas.ForceUpdateCanvases();s=arenaContent.rect.size;}
            return s.x<10?new Vector2(900,1500):s;
        }

        Vector2 ToArenaPixels(Vector2 p)
        {
            Vector2 s=ArenaPixelSize();
            return new Vector2(p.x*s.x*.5f,p.y*s.y*.5f);
        }

        Vector2 FromArenaPixels(Vector2 p)
        {
            Vector2 s=ArenaPixelSize();
            return new Vector2(p.x/(s.x*.5f),p.y/(s.y*.5f));
        }

        void SetArenaTransform(RectTransform rt,Vector2 pos,Vector2 size,float angle)
        {
            rt.anchorMin=rt.anchorMax=new Vector2(.5f,.5f);
            rt.pivot=new Vector2(.5f,.5f);
            rt.sizeDelta=size;
            rt.anchoredPosition=ToArenaPixels(pos);
            rt.localRotation=Quaternion.Euler(0,0,angle);
        }

        static Vector2 AngleDir(float deg)
        {
            float r=deg*Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r),Mathf.Sin(r));
        }

        static Vector2 Rotate(Vector2 v,float deg)
        {
            float r=deg*Mathf.Deg2Rad,c=Mathf.Cos(r),s=Mathf.Sin(r);
            return new Vector2(v.x*c-v.y*s,v.x*s+v.y*c);
        }

        void ClearLayers()
        {
            CancelInvoke();
            for(int i=worldLayer.childCount-1;i>=0;i--) Destroy(worldLayer.GetChild(i).gameObject);
            for(int i=overlayLayer.childCount-1;i>=0;i--) Destroy(overlayLayer.GetChild(i).gameObject);
            enemies.Clear();bullets.Clear();effects.Clear();obstacles.Clear();
            arena=null;arenaContent=null;playerRoot=null;playerModel=null;aimRing=null;
            heartsText=null;levelText=null;enemiesText=null;coinText=null;
        }

        RectTransform NewRect(string name,Transform parent)
        {
            var go=new GameObject(name,typeof(RectTransform));
            go.transform.SetParent(parent,false);
            return go.GetComponent<RectTransform>();
        }

        void Stretch(RectTransform rt,Vector2 min,Vector2 max)
        {
            rt.anchorMin=min;rt.anchorMax=max;rt.offsetMin=Vector2.zero;rt.offsetMax=Vector2.zero;
        }

        RectTransform AddPanel(Transform parent,string name,Vector2 min,Vector2 max,Color color,Sprite sprite)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image));
            go.transform.SetParent(parent,false);
            var rt=go.GetComponent<RectTransform>();Stretch(rt,min,max);
            var img=go.GetComponent<Image>();img.color=color;img.sprite=sprite;img.type=sprite!=null?Image.Type.Sliced:Image.Type.Simple;
            return rt;
        }

        RectTransform AddImage(Transform parent,string name,Vector2 min,Vector2 max,Color color,Sprite sprite)
        {
            return AddPanel(parent,name,min,max,color,sprite);
        }

        Text AddText(Transform parent,string content,int size,FontStyle style,TextAnchor align,Vector2 min,Vector2 max,Color color)
        {
            var go=new GameObject("Text "+content,typeof(RectTransform),typeof(Text));
            go.transform.SetParent(parent,false);
            var rt=go.GetComponent<RectTransform>();Stretch(rt,min,max);
            var t=go.GetComponent<Text>();
            t.font=font;t.text=content;t.fontSize=size;t.fontStyle=style;t.alignment=align;t.color=color;
            t.resizeTextForBestFit=true;t.resizeTextMinSize=Mathf.Max(12,size/2);t.resizeTextMaxSize=size;t.raycastTarget=false;
            return t;
        }

        RectTransform AddButton(Transform parent,string label,Vector2 min,Vector2 max,Color bgColor,Color textColor,Action action,int size)
        {
            var rt=AddPanel(parent,"Button "+label,min,max,bgColor,rounded);
            var btn=rt.gameObject.AddComponent<Button>();
            btn.targetGraphic=rt.GetComponent<Image>();
            btn.onClick.AddListener(()=>action());
            var outline=rt.gameObject.AddComponent<Outline>();outline.effectColor=new Color(0,0,0,.38f);outline.effectDistance=new Vector2(2,-2);
            AddText(rt,label,size,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.04f,.08f),new Vector2(.96f,.92f),textColor);
            return rt;
        }

        void AddChip(Transform parent,string text,Vector2 min,Vector2 max,Color accent)
        {
            var chip=AddPanel(parent,"Chip",min,max,new Color(.045f,.06f,.075f,.98f),rounded);
            AddImage(chip,"Dot",new Vector2(.07f,.30f),new Vector2(.13f,.70f),accent,circle);
            AddText(chip,text,22,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.15f,.08f),new Vector2(.95f,.92f),white);
        }

        Sprite MakeRoundedSprite(int size,float radius)
        {
            var tex=new Texture2D(size,size,TextureFormat.RGBA32,false);
            tex.name="RR2 Rounded";tex.wrapMode=TextureWrapMode.Clamp;tex.filterMode=FilterMode.Bilinear;
            var pixels=new Color32[size*size];
            for(int y=0;y<size;y++)
            for(int x=0;x<size;x++)
            {
                float px=Mathf.Max(0,Mathf.Max(radius-x,x-(size-1-radius)));
                float py=Mathf.Max(0,Mathf.Max(radius-y,y-(size-1-radius)));
                float dist=Mathf.Sqrt(px*px+py*py);
                byte a=(byte)Mathf.RoundToInt(Mathf.Clamp01(radius+1f-dist)*255f);
                pixels[y*size+x]=new Color32(255,255,255,a);
            }
            tex.SetPixels32(pixels);tex.Apply();
            return Sprite.Create(tex,new Rect(0,0,size,size),new Vector2(.5f,.5f),100f,0,SpriteMeshType.FullRect,new Vector4(radius,radius,radius,radius));
        }
    }
}
