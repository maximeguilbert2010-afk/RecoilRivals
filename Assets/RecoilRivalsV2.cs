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

        const int MaxLevel = 30;
        const int ShotgunUnlockAfterLevel = 15;

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

        [SerializeField] public Material menuLitTemplate;
        [SerializeField] public GameObject menuWeaponPrefab;
        [SerializeField] public Material menuWeaponMaterial;
        [SerializeField] public GameObject menuShotgunPrefab;
        [SerializeField] public Material menuShotgunMaterial;
        GameObject menu3DRoot;
        Camera menuCamera;
        Transform menuGunRoot;
        Transform menuGunModel;
        float menuAnimTime;
        float menuYaw = 18f;
        float menuPitch = 4f;
        float menuZoom = 8.4f;
        float menuMinZoom = 6.8f;
        float menuMaxZoom = 11.5f;
        bool menuDragging;
        float lastPinchDistance;

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

            level = Mathf.Clamp(PlayerPrefs.GetInt("RR2_Level", 1), 1, MaxLevel);
            coins = Mathf.Max(0, PlayerPrefs.GetInt("RR2_Coins", 0));
            selectedWeapon = (WeaponType)Mathf.Clamp(PlayerPrefs.GetInt("RR2_Weapon", 0), 0, 2);
            // Only weapons with real 3D assets are selectable right now.
            if (selectedWeapon == WeaponType.Revolver || (selectedWeapon == WeaponType.Shotgun && !ShotgunUnlocked()))
                selectedWeapon = WeaponType.Pistol;

            weapons = new[]
            {
                new WeaponStats("9MM PISTOL",1f,.25f,.42f,92f,1.55f,1,0f,0),
                new WeaponStats("IRON REVOLVER",2f,.48f,.62f,138f,1.72f,1,0f,1),
                new WeaponStats("BREACH SHOTGUN",.72f,.72f,.82f,176f,1.34f,6,13f,0)
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
            if ((mode == ScreenMode.Menu || mode == ScreenMode.Loadout) && menuGunRoot != null)
            {
                UpdateMenuWeaponInput();

                if (!menuDragging)
                {
                    menuAnimTime += Time.unscaledDeltaTime;
                    menuYaw += Time.unscaledDeltaTime * 5.5f;
                }

                menuGunRoot.localRotation = Quaternion.Euler(menuPitch, menuYaw, 0f);
                menuGunRoot.localPosition = new Vector3(0f, .02f + Mathf.Sin(menuAnimTime * .8f) * .025f, 0f);

                if (menuCamera != null)
                {
                    menuCamera.transform.position = new Vector3(0f, .16f, -menuZoom);
                    menuCamera.transform.LookAt(new Vector3(0f,.10f,0f));
                }
            }

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

            // The menu background is a real 3D armory scene rendered by a camera.
            // UI is kept transparent so the actual weapon remains visible.
            BuildMenu3DView(null);

            AddImage(worldLayer,"Top Shade",new Vector2(0,.73f),new Vector2(1,1),new Color(.006f,.010f,.015f,.58f),null);
            AddImage(worldLayer,"Bottom Shade",new Vector2(0,0),new Vector2(1,.31f),new Color(.006f,.010f,.015f,.74f),null);

            AddText(worldLayer,"RECOIL",82,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.08f,.835f),new Vector2(.92f,.915f),white);
            AddText(worldLayer,"R  I  V  A  L  S",31,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.12f,.795f),new Vector2(.88f,.838f),cyan);
            AddText(worldLayer,"TACTICAL RECOIL DUELS",16,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.22f,.765f),new Vector2(.78f,.795f),new Color(.48f,.61f,.65f,1f));

            AddTechChip(worldLayer,"LV "+level,new Vector2(.055f,.925f),new Vector2(.275f,.972f),cyan);
            AddTechChip(worldLayer,"◈  "+coins,new Vector2(.725f,.925f),new Vector2(.945f,.972f),orange);

            var info=AddPanel(worldLayer,"Weapon Info",new Vector2(.07f,.275f),new Vector2(.93f,.355f),new Color(.015f,.022f,.030f,.80f),rounded);
            var infoOutline=info.gameObject.AddComponent<Outline>();
            infoOutline.effectColor=new Color(.12f,.63f,.60f,.46f);
            infoOutline.effectDistance=new Vector2(1.5f,-1.5f);
            AddText(info,SelectedWeaponDisplayName(),27,FontStyle.Bold,TextAnchor.MiddleLeft,new Vector2(.05f,.43f),new Vector2(.52f,.88f),white);
            AddText(info,"REAL 3D VIEW",15,FontStyle.Bold,TextAnchor.MiddleLeft,new Vector2(.05f,.10f),new Vector2(.42f,.44f),cyanSoft);
            AddText(info,"DRAG TO ROTATE   •   PINCH TO ZOOM",15,FontStyle.Bold,TextAnchor.MiddleRight,new Vector2(.38f,.12f),new Vector2(.95f,.74f),muted);

            AddTechButton(worldLayer,"PLAY",new Vector2(.075f,.155f),new Vector2(.925f,.245f),true,()=>StartLevel(level),38);
            AddTechButton(worldLayer,"LOADOUT",new Vector2(.18f,.080f),new Vector2(.82f,.137f),false,ShowLoadout,24);
            AddText(worldLayer,"360° WEAPON INSPECTION",14,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.20f,.030f),new Vector2(.80f,.060f),new Color(.31f,.45f,.49f,1f));
        }

        void ShowLoadout()
        {
            mode = ScreenMode.Loadout;
            ClearLayers();
            BuildMenu3DView(null);

            AddImage(worldLayer,"Loadout Top Shade",new Vector2(0,.72f),new Vector2(1,1),new Color(.006f,.010f,.015f,.72f),null);
            AddImage(worldLayer,"Loadout Bottom Shade",new Vector2(0,0),new Vector2(1,.38f),new Color(.006f,.010f,.015f,.86f),null);

            AddText(worldLayer,"LOADOUT",58,FontStyle.Bold,TextAnchor.MiddleLeft,new Vector2(.06f,.90f),new Vector2(.62f,.965f),white);
            AddText(worldLayer,"REAL 3D WEAPON VIEW",16,FontStyle.Bold,TextAnchor.MiddleLeft,new Vector2(.06f,.865f),new Vector2(.55f,.905f),cyanSoft);
            AddText(worldLayer,"DRAG TO ROTATE  •  PINCH TO ZOOM",14,FontStyle.Bold,TextAnchor.MiddleRight,new Vector2(.39f,.865f),new Vector2(.94f,.905f),muted);

            bool shotgunUnlocked=ShotgunUnlocked();

            var pistol=AddPanel(worldLayer,"Pistol Select",new Vector2(.055f,.215f),new Vector2(.475f,.345f),
                selectedWeapon==WeaponType.Pistol?new Color(.035f,.13f,.125f,.96f):new Color(.025f,.035f,.047f,.96f),rounded);
            AddText(pistol,"9MM PISTOL",25,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.05f,.52f),new Vector2(.95f,.88f),white);
            AddText(pistol,"STARTER",14,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.08f,.20f),new Vector2(.92f,.48f),cyanSoft);
            AddButton(pistol,selectedWeapon==WeaponType.Pistol?"EQUIPPED":"EQUIP",new Vector2(.17f,-.42f),new Vector2(.83f,.02f),
                selectedWeapon==WeaponType.Pistol?cyan:panel2,selectedWeapon==WeaponType.Pistol?bg:white,
                ()=>EquipWeapon(WeaponType.Pistol),18);

            var shotgun=AddPanel(worldLayer,"Shotgun Select",new Vector2(.525f,.215f),new Vector2(.945f,.345f),
                selectedWeapon==WeaponType.Shotgun?new Color(.14f,.075f,.025f,.96f):new Color(.025f,.035f,.047f,.96f),rounded);
            AddText(shotgun,"BREACH SHOTGUN",23,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.04f,.52f),new Vector2(.96f,.88f),white);
            AddText(shotgun,shotgunUnlocked?"UNLOCKED":"UNLOCK AFTER LEVEL 15",14,FontStyle.Bold,TextAnchor.MiddleCenter,
                new Vector2(.05f,.20f),new Vector2(.95f,.48f),shotgunUnlocked?orange:muted);
            AddButton(shotgun,shotgunUnlocked?(selectedWeapon==WeaponType.Shotgun?"EQUIPPED":"EQUIP"):"LOCKED",
                new Vector2(.17f,-.42f),new Vector2(.83f,.02f),
                shotgunUnlocked?(selectedWeapon==WeaponType.Shotgun?orange:panel2):new Color(.055f,.060f,.068f,1f),
                shotgunUnlocked?(selectedWeapon==WeaponType.Shotgun?bg:white):muted,
                ()=>{ if(ShotgunUnlocked()) EquipWeapon(WeaponType.Shotgun); },18);

            var stats=AddPanel(worldLayer,"Weapon Stats",new Vector2(.07f,.105f),new Vector2(.93f,.185f),new Color(.015f,.022f,.030f,.84f),rounded);
            var s=weapons[(int)selectedWeapon];
            AddText(stats,SelectedWeaponDisplayName(),20,FontStyle.Bold,TextAnchor.MiddleLeft,new Vector2(.04f,.52f),new Vector2(.46f,.90f),white);
            AddText(stats,"DMG "+s.damage.ToString("0.00")+"   RECOIL "+Mathf.RoundToInt(s.recoil*100)+"   PELLETS "+s.pellets,
                15,FontStyle.Bold,TextAnchor.MiddleRight,new Vector2(.35f,.14f),new Vector2(.96f,.60f),muted);

            AddButton(worldLayer,"BACK",new Vector2(.25f,.030f),new Vector2(.75f,.082f),panel2,white,ShowMenu,24);
        }

        bool ShotgunUnlocked()
        {
            // "After 15 levels" means the reward is available once level 15 has been completed.
            return level > ShotgunUnlockAfterLevel;
        }

        string SelectedWeaponDisplayName()
        {
            return selectedWeapon==WeaponType.Shotgun ? "BREACH SHOTGUN" : "9MM PISTOL";
        }

        void EquipWeapon(WeaponType weapon)
        {
            if(weapon==WeaponType.Shotgun && !ShotgunUnlocked()) return;
            if(weapon==WeaponType.Revolver) return;

            selectedWeapon=weapon;
            PlayerPrefs.SetInt("RR2_Weapon",(int)selectedWeapon);
            PlayerPrefs.Save();
            ShowLoadout();
        }

        void StartLevel(int target)
        {
            mode = ScreenMode.Playing;
            resolving = false;
            level = Mathf.Clamp(target,1,MaxLevel);
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
            AddText(tag,"SECTOR "+level.ToString("00"),16,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.05f,.1f),new Vector2(.95f,.9f),cyanSoft);
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
            if(completed<MaxLevel) level=completed+1;
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

            if(victory && completed==ShotgunUnlockAfterLevel)
                AddText(dim,"NEW WEAPON UNLOCKED  •  BREACH SHOTGUN",22,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.08f,.535f),new Vector2(.92f,.585f),orange);

            if(victory && completed<MaxLevel)
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

        void BuildMenu3DView(RectTransform bay)
        {
            DestroyMenu3D();

            menu3DRoot = new GameObject("REAL_3D_MENU_SCENE");
            menu3DRoot.transform.SetParent(transform,false);

            // Direct scene camera: the gun is NOT rendered to a RawImage or texture.
            var camGO=new GameObject("Menu 3D Camera");
            camGO.transform.SetParent(menu3DRoot.transform,false);
            menuCamera=camGO.AddComponent<Camera>();
            menuCamera.tag="MainCamera";
            menuCamera.clearFlags=CameraClearFlags.SolidColor;
            menuCamera.backgroundColor=new Color(.006f,.009f,.014f,1f);
            menuCamera.fieldOfView=31f;
            menuCamera.nearClipPlane=.03f;
            menuCamera.farClipPlane=60f;
            menuCamera.depth=-20f;
            menuCamera.allowHDR=true;
            menuCamera.transform.position=new Vector3(0f,.16f,-menuZoom);
            menuCamera.transform.LookAt(new Vector3(0f,.10f,0f));

            Material baseMat = menuLitTemplate != null ? menuLitTemplate : menuWeaponMaterial;
            if(baseMat==null) throw new InvalidOperationException("3D menu material is missing.");

            Material floorMat=CloneMenuMat(baseMat,new Color(.028f,.032f,.038f),.68f,.54f,Color.black);
            Material wallMat=CloneMenuMat(baseMat,new Color(.018f,.025f,.031f),.55f,.40f,Color.black);
            Material edgeMat=CloneMenuMat(baseMat,new Color(.025f,.20f,.20f),.45f,.62f,new Color(.02f,.55f,.48f)*.7f);
            Material darkMat=CloneMenuMat(baseMat,new Color(.010f,.013f,.017f),.48f,.48f,Color.black);

            // Actual 3D armory/hangar set.
            CreatePart(menu3DRoot.transform,"Floor",new Vector3(0f,-1.58f,2.8f),new Vector3(11f,.12f,10f),floorMat,null);
            CreatePart(menu3DRoot.transform,"Back Wall",new Vector3(0f,1.45f,3.7f),new Vector3(11f,6.2f,.18f),wallMat,null);
            CreatePart(menu3DRoot.transform,"Left Wall",new Vector3(-4.5f,.4f,1.8f),new Vector3(.16f,4.8f,4.2f),darkMat,null);
            CreatePart(menu3DRoot.transform,"Right Wall",new Vector3(4.5f,.4f,1.8f),new Vector3(.16f,4.8f,4.2f),darkMat,null);

            for(int i=-3;i<=3;i++)
            {
                CreatePart(menu3DRoot.transform,"Back Rib "+i,new Vector3(i*1.22f,.65f,3.56f),new Vector3(.07f,4.2f,.12f),edgeMat,null);
                CreatePart(menu3DRoot.transform,"Floor Rail "+i,new Vector3(i*1.25f,-1.50f,1.6f),new Vector3(.035f,.025f,5.8f),edgeMat,null);
            }

            // Showcase pedestal.
            CreateCylinder(menu3DRoot.transform,"Pedestal Base",new Vector3(0f,-1.38f,.20f),new Vector3(2.45f,.18f,1.38f),darkMat,64);
            CreateCylinder(menu3DRoot.transform,"Pedestal Light",new Vector3(0f,-1.20f,.20f),new Vector3(2.24f,.045f,1.22f),edgeMat,64);
            CreateCylinder(menu3DRoot.transform,"Pedestal Deck",new Vector3(0f,-1.12f,.20f),new Vector3(2.07f,.065f,1.12f),floorMat,64);

            // Key/fill/rim lighting around the real model.
            var keyGO=new GameObject("Key Light");
            keyGO.transform.SetParent(menu3DRoot.transform,false);
            keyGO.transform.rotation=Quaternion.Euler(32f,-36f,0f);
            var key=keyGO.AddComponent<Light>();
            key.type=LightType.Directional; key.intensity=1.55f; key.color=new Color(.83f,.90f,1f);
            key.shadows=LightShadows.Soft;

            var rimGO=new GameObject("Cyan Rim");
            rimGO.transform.SetParent(menu3DRoot.transform,false);
            rimGO.transform.position=new Vector3(-2.7f,1.3f,-1.5f);
            var rim=rimGO.AddComponent<Light>();
            rim.type=LightType.Point; rim.range=8f; rim.intensity=4.2f; rim.color=new Color(.04f,.85f,.78f);

            var warmGO=new GameObject("Warm Fill");
            warmGO.transform.SetParent(menu3DRoot.transform,false);
            warmGO.transform.position=new Vector3(2.8f,-.35f,-.8f);
            var warm=warmGO.AddComponent<Light>();
            warm.type=LightType.Point; warm.range=7f; warm.intensity=2.1f; warm.color=new Color(1f,.35f,.12f);

            GameObject selectedPrefab = selectedWeapon==WeaponType.Shotgun ? menuShotgunPrefab : menuWeaponPrefab;
            Material selectedMaterial = selectedWeapon==WeaponType.Shotgun ? menuShotgunMaterial : menuWeaponMaterial;

            if(selectedPrefab==null)
                throw new InvalidOperationException("The selected real 3D weapon asset was not assigned to the menu.");

            menuGunRoot=new GameObject("Weapon Rotation Pivot").transform;
            menuGunRoot.SetParent(menu3DRoot.transform,false);
            menuGunRoot.localPosition=Vector3.zero;

            var model=Instantiate(selectedPrefab,menuGunRoot);
            model.name=selectedWeapon==WeaponType.Shotgun ? "Breach Shotgun - REAL FBX" : "Pistol 9mm - REAL FBX";
            menuGunModel=model.transform;

            foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if(selectedMaterial!=null)
                {
                    var mats=new Material[Mathf.Max(1,renderer.sharedMaterials.Length)];
                    for(int i=0;i<mats.Length;i++) mats[i]=selectedMaterial;
                    renderer.sharedMaterials=mats;
                }
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows=true;
            }

            NormalizeMenuWeapon(model);

            menuYaw=18f;
            menuPitch=4f;
            menuAnimTime=0f;
            menuDragging=false;
            lastPinchDistance=0f;
        }

        void NormalizeMenuWeapon(GameObject model)
        {
            model.transform.localPosition=Vector3.zero;
            model.transform.localRotation=Quaternion.identity;
            model.transform.localScale=Vector3.one;

            Bounds b=GetRendererBounds(model);
            Vector3 s=b.size;

            // Keep the weapon's longest axis horizontal in portrait.
            if(s.z>s.x && s.z>s.y)
                model.transform.localRotation=Quaternion.Euler(0f,90f,0f);
            else if(s.y>s.x && s.y>s.z)
                model.transform.localRotation=Quaternion.Euler(0f,0f,90f);

            // Portrait screens are narrow. The old 3.85-unit target was almost
            // twice as wide as the camera frustum, which is why the player only
            // saw giant clipped pieces of the pistol.
            b=GetRendererBounds(model);
            float longest=Mathf.Max(b.size.x,Mathf.Max(b.size.y,b.size.z));
            if(longest>.0001f)
                model.transform.localScale=Vector3.one*(2.05f/longest);

            // Center the actual renderer bounds on the rotation pivot.
            b=GetRendererBounds(model);
            model.transform.position-=b.center;
            model.transform.localPosition+=new Vector3(0f,.10f,0f);

            // Fit a bounding sphere inside the *horizontal* FOV so the complete
            // gun stays visible even while the player rotates it through 360°.
            b=GetRendererBounds(model);
            float radius=Mathf.Max(.2f,b.extents.magnitude);
            float aspect=(Screen.height>0)?Mathf.Clamp((float)Screen.width/Screen.height,.42f,.75f):(9f/16f);
            float vHalf=menuCamera.fieldOfView*.5f*Mathf.Deg2Rad;
            float hHalf=Mathf.Atan(Mathf.Tan(vHalf)*aspect);
            float limitingHalf=Mathf.Max(.08f,Mathf.Min(vHalf,hHalf));
            float fitted=radius/Mathf.Tan(limitingHalf)*1.20f;

            menuZoom=Mathf.Clamp(fitted,7.6f,11.2f);
            menuMinZoom=menuZoom*.78f;
            menuMaxZoom=menuZoom*1.36f;
        }

        Bounds GetRendererBounds(GameObject model)
        {
            var rs=model.GetComponentsInChildren<Renderer>(true);
            if(rs.Length==0) return new Bounds(model.transform.position,Vector3.one);
            Bounds b=rs[0].bounds;
            for(int i=1;i<rs.Length;i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        void UpdateMenuWeaponInput()
        {
            menuDragging=false;

            if(Touchscreen.current!=null)
            {
                var touches=Touchscreen.current.touches;
                bool t0=touches.Count>0 && touches[0].press.isPressed;
                bool t1=touches.Count>1 && touches[1].press.isPressed;

                if(t0 && t1)
                {
                    Vector2 p0=touches[0].position.ReadValue();
                    Vector2 p1=touches[1].position.ReadValue();
                    float dist=Vector2.Distance(p0,p1);
                    if(lastPinchDistance>1f)
                    {
                        float delta=dist-lastPinchDistance;
                        menuZoom=Mathf.Clamp(menuZoom-delta*.0065f,menuMinZoom,menuMaxZoom);
                    }
                    lastPinchDistance=dist;
                    menuDragging=true;
                    return;
                }

                lastPinchDistance=0f;
                if(t0)
                {
                    Vector2 p=touches[0].position.ReadValue();
                    // Reserve the upper title and lower buttons for UI taps.
                    if(p.y>Screen.height*.30f && p.y<Screen.height*.78f)
                    {
                        Vector2 d=touches[0].delta.ReadValue();
                        menuYaw-=d.x*.22f;
                        menuPitch=Mathf.Clamp(menuPitch+d.y*.14f,-34f,34f);
                        menuDragging=d.sqrMagnitude>.01f;
                    }
                }
                return;
            }

            if(Mouse.current!=null)
            {
                Vector2 p=Mouse.current.position.ReadValue();
                if(Mouse.current.leftButton.isPressed && p.y>Screen.height*.30f && p.y<Screen.height*.78f)
                {
                    Vector2 d=Mouse.current.delta.ReadValue();
                    menuYaw-=d.x*.22f;
                    menuPitch=Mathf.Clamp(menuPitch+d.y*.14f,-34f,34f);
                    menuDragging=d.sqrMagnitude>.01f;
                }

                float scroll=Mouse.current.scroll.ReadValue().y;
                if(Mathf.Abs(scroll)>1f) menuZoom=Mathf.Clamp(menuZoom-scroll*.0025f,menuMinZoom,menuMaxZoom);
            }
        }

        Material CloneMenuMat(Material source,Color color,float metallic,float smoothness,Color emission)
        {
            var m=new Material(source);
            if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color);
            if(m.HasProperty("_Color"))m.SetColor("_Color",color);
            if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",metallic);
            if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",smoothness);
            if(emission.maxColorComponent>.001f && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor",emission);
            }
            return m;
        }

        void CreateMenuGunModel(Transform root,int weapon,Material dark,Material metal,Material lightMetal,Material accent,Material black)
        {
            float longBody = weapon==2 ? 3.55f : weapon==1 ? 2.65f : 2.95f;
            float barrel = weapon==2 ? 1.65f : weapon==1 ? 1.35f : 1.50f;

            // Main receiver - intentionally layered like a designed hard-surface asset.
            CreatePart(root,"Receiver Core",new Vector3(-.12f,.08f,0),new Vector3(longBody,.62f,.58f),metal,null);
            CreatePart(root,"Upper Receiver",new Vector3(.12f,.38f,-.02f),new Vector3(longBody*.82f,.20f,.50f),lightMetal,null);
            CreatePart(root,"Lower Receiver",new Vector3(-.24f,-.22f,.02f),new Vector3(longBody*.60f,.22f,.46f),dark,null);
            CreatePart(root,"Rear Block",new Vector3(-longBody*.55f,.10f,.02f),new Vector3(.64f,.70f,.62f),black,null);

            // Barrel assembly.
            CreateCylinder(root,"Outer Barrel",new Vector3(longBody*.56f+barrel*.40f,.17f,0),new Vector3(.26f,barrel*.50f,.26f),dark,32,Quaternion.Euler(0,0,90));
            CreateCylinder(root,"Inner Barrel",new Vector3(longBody*.56f+barrel*.84f,.17f,0),new Vector3(.15f,barrel*.18f,.15f),accent,32,Quaternion.Euler(0,0,90));
            CreateCylinder(root,"Muzzle",new Vector3(longBody*.56f+barrel,.17f,0),new Vector3(.34f,.18f,.34f),black,32,Quaternion.Euler(0,0,90));

            // Handguard and lower rail.
            CreatePart(root,"Handguard",new Vector3(longBody*.38f,-.06f,0),new Vector3(longBody*.55f,.42f,.68f),dark,null);
            for(int i=0;i<4;i++)
                CreatePart(root,"Rail Vent "+i,new Vector3(longBody*.18f+i*.34f,.31f,-.31f),new Vector3(.20f,.07f,.04f),accent,null);

            // Grip with real depth and angle.
            var grip=CreatePart(root,"Grip",new Vector3(-.35f,-.70f,.03f),new Vector3(.48f,1.15f,.50f),black,null);
            grip.transform.localRotation=Quaternion.Euler(0,0,-14f);
            CreatePart(grip.transform,"Grip Insert",new Vector3(0,-.05f,-.27f),new Vector3(.30f,.76f,.05f),dark,null);

            // Magazine/cylinder distinguishes weapon silhouettes.
            if(weapon==1)
            {
                CreateCylinder(root,"Revolver Cylinder",new Vector3(.15f,-.12f,0),new Vector3(.46f,.40f,.46f),lightMetal,24,Quaternion.Euler(90,0,0));
                for(int i=0;i<6;i++)
                {
                    float a=i*60f*Mathf.Deg2Rad;
                    CreateCylinder(root,"Chamber "+i,new Vector3(.15f+Mathf.Cos(a)*.24f,-.12f,Mathf.Sin(a)*.24f),new Vector3(.08f,.12f,.08f),black,16,Quaternion.Euler(90,0,0));
                }
            }
            else
            {
                var mag=CreatePart(root,"Magazine",new Vector3(.34f,-.72f,.02f),new Vector3(.58f,1.18f,.46f),black,null);
                mag.transform.localRotation=Quaternion.Euler(0,0,weapon==2?4f:8f);
                CreatePart(mag.transform,"Magazine Accent",new Vector3(.03f,-.04f,-.26f),new Vector3(.32f,.72f,.05f),accent,null);
            }

            // Sight and top rail.
            CreatePart(root,"Top Rail",new Vector3(-.10f,.56f,0),new Vector3(longBody*.65f,.08f,.38f),black,null);
            CreatePart(root,"Rear Sight",new Vector3(-.72f,.72f,0),new Vector3(.24f,.30f,.42f),dark,null);
            CreatePart(root,"Front Sight",new Vector3(.86f,.67f,0),new Vector3(.18f,.25f,.36f),dark,null);

            // Stock for shotgun, compact rear brace for others.
            if(weapon==2)
            {
                var stock=CreatePart(root,"Stock",new Vector3(-2.18f,-.02f,0),new Vector3(1.25f,.52f,.60f),dark,null);
                stock.transform.localRotation=Quaternion.Euler(0,0,7f);
                CreatePart(root,"Stock Pad",new Vector3(-2.82f,-.12f,0),new Vector3(.22f,.86f,.68f),black,null);
            }
            else
            {
                CreatePart(root,"Rear Brace",new Vector3(-1.78f,.08f,0),new Vector3(.82f,.30f,.50f),dark,null);
                CreatePart(root,"Brace Accent",new Vector3(-1.94f,.08f,-.28f),new Vector3(.40f,.09f,.04f),accent,null);
            }

            // Small restrained accent strips.
            CreatePart(root,"Side Accent A",new Vector3(.05f,.12f,-.31f),new Vector3(1.05f,.08f,.035f),accent,null);
            CreatePart(root,"Side Accent B",new Vector3(-.80f,-.08f,-.31f),new Vector3(.42f,.06f,.035f),accent,null);
        }

        GameObject CreatePart(Transform parent,string name,Vector3 pos,Vector3 scale,Material mat,Quaternion? rotation)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name=name;
            go.transform.SetParent(parent,false);
            go.transform.localPosition=pos;
            go.transform.localScale=scale;
            if(rotation.HasValue)go.transform.localRotation=rotation.Value;
            var col=go.GetComponent<Collider>();if(col!=null)Destroy(col);
            var renderer=go.GetComponent<Renderer>();
            renderer.sharedMaterial=mat;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows=true;
            return go;
        }

        GameObject CreateCylinder(Transform parent,string name,Vector3 pos,Vector3 scale,Material mat,int segments)
        {
            return CreateCylinder(parent,name,pos,scale,mat,segments,Quaternion.identity);
        }

        GameObject CreateCylinder(Transform parent,string name,Vector3 pos,Vector3 scale,Material mat,int segments,Quaternion rotation)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name=name;
            go.transform.SetParent(parent,false);
            go.transform.localPosition=pos;
            go.transform.localScale=scale;
            go.transform.localRotation=rotation;
            var col=go.GetComponent<Collider>();if(col!=null)Destroy(col);
            var renderer=go.GetComponent<Renderer>();
            renderer.sharedMaterial=mat;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows=true;
            return go;
        }

        void DestroyMenu3D()
        {
            menuGunRoot=null;
            menuGunModel=null;
            menuCamera=null;
            if(menu3DRoot!=null)
            {
                Destroy(menu3DRoot);
                menu3DRoot=null;
            }
        }

        RectTransform AddTechChip(Transform parent,string label,Vector2 min,Vector2 max,Color accent)
        {
            var chip=AddPanel(parent,"Tech Chip "+label,min,max,new Color(.028f,.040f,.052f,.98f),rounded);
            var outline=chip.gameObject.AddComponent<Outline>();
            outline.effectColor=new Color(accent.r,accent.g,accent.b,.52f);
            outline.effectDistance=new Vector2(1.4f,-1.4f);
            AddImage(chip,"Chip Accent",new Vector2(.025f,.20f),new Vector2(.055f,.80f),accent,rounded);
            AddText(chip,label,19,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.08f,.08f),new Vector2(.95f,.92f),white);
            return chip;
        }

        RectTransform AddTechButton(Transform parent,string label,Vector2 min,Vector2 max,bool primary,Action action,int fontSize)
        {
            Color fill=primary?new Color(.055f,.72f,.64f,1f):new Color(.032f,.048f,.062f,1f);
            Color edge=primary?new Color(.17f,1f,.86f,1f):new Color(.15f,.46f,.49f,1f);
            Color txt=primary?new Color(.005f,.035f,.035f,1f):white;

            var outer=AddPanel(parent,"Tech Button "+label,min,max,new Color(edge.r,edge.g,edge.b,.70f),rounded);
            var inner=AddPanel(outer,"Inner",new Vector2(.008f,.055f),new Vector2(.992f,.945f),fill,rounded);
            AddImage(inner,"Left Accent",new Vector2(.018f,.18f),new Vector2(.032f,.82f),edge,rounded);
            AddImage(inner,"Right Accent",new Vector2(.968f,.18f),new Vector2(.982f,.82f),edge,rounded);
            AddImage(inner,"Bottom Accent",new Vector2(.08f,.045f),new Vector2(.92f,.065f),new Color(edge.r,edge.g,edge.b,.65f),rounded);
            AddText(inner,label,fontSize,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.07f,.08f),new Vector2(.87f,.92f),txt);
            AddText(inner,primary?"›":"//",fontSize,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.84f,.08f),new Vector2(.97f,.92f),primary?txt:edge);

            var btn=outer.gameObject.AddComponent<Button>();
            btn.targetGraphic=outer.GetComponent<Image>();
            btn.onClick.AddListener(()=>action());
            var cb=btn.colors;
            cb.normalColor=Color.white;
            cb.highlightedColor=new Color(.94f,1f,1f,1f);
            cb.pressedColor=new Color(.70f,.85f,.85f,1f);
            cb.selectedColor=Color.white;
            cb.fadeDuration=.08f;
            btn.colors=cb;
            return outer;
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
            DestroyMenu3D();
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
