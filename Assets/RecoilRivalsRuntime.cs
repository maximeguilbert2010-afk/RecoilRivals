using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RecoilRivals
{
    public enum RRWeaponType { Pistol, Revolver, Shotgun }
    public enum RREnemyType { Standard, Fast, Heavy, Shotgun }

    [Serializable]
    public struct RRWeaponStats
    {
        public string name;
        public float damage, fireDelay, recoil, torque, bulletSpeed, spread;
        public int pellets, bounces;

        public RRWeaponStats(string name, float damage, float fireDelay, float recoil, float torque, float bulletSpeed, int pellets, float spread, int bounces)
        {
            this.name = name; this.damage = damage; this.fireDelay = fireDelay;
            this.recoil = recoil; this.torque = torque; this.bulletSpeed = bulletSpeed;
            this.pellets = pellets; this.spread = spread; this.bounces = bounces;
        }
    }

    public static class RRBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (UnityEngine.Object.FindFirstObjectByType<RecoilRivalsGame>() != null) return;
            var root = new GameObject("RECOIL_RIVALS_RUNTIME");
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.AddComponent<RecoilRivalsGame>();
        }
    }

    public class RecoilRivalsGame : MonoBehaviour
    {
        public enum Mode { Menu, Weapons, Playing, Victory, Defeat }
        public Mode CurrentMode { get; private set; }
        public PlayerGun Player { get; private set; }
        public RRWeaponStats[] Weapons { get; private set; }

        Camera cam;
        Canvas canvas;
        RectTransform safeRoot;
        Transform worldRoot, menuGun;
        Font font;
        Sprite roundedSprite;
        PhysicsMaterial2D bounceMat;
        AudioSource audioSource;
        AudioClip shotClip, hitClip, winClip;

        Material arenaMat, arenaAccentMat, playerMat, playerAccentMat;
        Material enemyMat, enemyFastMat, enemyHeavyMat, enemyShotgunMat;
        Material playerBulletMat, enemyBulletMat, glowMat;

        readonly List<EnemyUnit> enemies = new List<EnemyUnit>();
        readonly Vector2[] enemySpots = {
            new Vector2(2.30f, 4.55f), new Vector2(-2.20f, 2.60f),
            new Vector2(2.10f, 0.35f), new Vector2(-2.25f, -1.75f)
        };

        int currentLevel, coins, selectedWeapon;
        Text hpText, levelText, enemyText, coinText;
        float menuTime, shakeTime, shakeStrength;
        Vector3 cameraHome;
        bool finishing;

        string startupError;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            try
            {
                // Remove the untouched Unity template immediately so a startup error can never
                // silently leave the sample scene looking like the game loaded correctly.
                RemoveTemplateScene();

                Application.targetFrameRate = 120;
                QualitySettings.vSyncCount = 0;
                Screen.orientation = ScreenOrientation.Portrait;
                Physics2D.gravity = new Vector2(0f, -9.81f);

                currentLevel = Mathf.Clamp(PlayerPrefs.GetInt("RR_Level", 1), 1, 10);
                coins = Mathf.Max(0, PlayerPrefs.GetInt("RR_Coins", 0));
                selectedWeapon = Mathf.Clamp(PlayerPrefs.GetInt("RR_Weapon", 0), 0, 2);

                Weapons = new[] {
                    new RRWeaponStats("STARTER PISTOL", 1f, .24f, 3.35f, 8.5f, 15f, 1, 0f, 0),
                    new RRWeaponStats("REVOLVER", 2f, .48f, 5.3f, 13f, 18f, 1, 0f, 1),
                    new RRWeaponStats("SHOTGUN", .7f, .70f, 7.4f, 17f, 13f, 5, 13f, 0)
                };

                CreateAssets();
                CreateCameraLightAndUI();
                ShowMenu();
            }
            catch (Exception ex)
            {
                startupError = ex.ToString();
                Debug.LogException(ex);
                EnsureFallbackCamera();
            }
        }

        void EnsureFallbackCamera()
        {
            if (Camera.main != null) return;
            var c = new GameObject("RR Fallback Camera");
            c.tag = "MainCamera";
            cam = c.AddComponent<Camera>();
            c.AddComponent<AudioListener>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.015f, .02f, .03f);
            cam.transform.position = new Vector3(0, 0, -10);
            DontDestroyOnLoad(c);
        }

        void OnGUI()
        {
            if (string.IsNullOrEmpty(startupError)) return;
            GUI.color = new Color(.08f, .08f, .10f, .98f);
            GUI.Box(new Rect(20, 60, Screen.width - 40, Screen.height - 120), GUIContent.none);
            GUI.color = Color.white;
            var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(20, Screen.width / 32), wordWrap = true };
            style.normal.textColor = new Color(1f, .45f, .40f);
            GUI.Label(new Rect(45, 90, Screen.width - 90, Screen.height - 180),
                "RECOIL RIVALS STARTUP ERROR\n\n" + startupError, style);
        }

        void CreateAssets()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            roundedSprite = CreateRoundedSprite();
            bounceMat = new PhysicsMaterial2D("RR_Bounce") { bounciness = .58f, friction = .08f };

            arenaMat = MakeMaterial(new Color(.07f, .09f, .12f), .0f);
            arenaAccentMat = MakeMaterial(new Color(.10f, .18f, .22f), .0f);
            playerMat = MakeMaterial(new Color(.05f, .70f, .76f), .1f);
            playerAccentMat = MakeMaterial(new Color(.60f, 1f, .95f), .45f);
            enemyMat = MakeMaterial(new Color(.90f, .18f, .16f), .1f);
            enemyFastMat = MakeMaterial(new Color(1f, .43f, .08f), .12f);
            enemyHeavyMat = MakeMaterial(new Color(.58f, .15f, .82f), .1f);
            enemyShotgunMat = MakeMaterial(new Color(.95f, .08f, .42f), .12f);
            playerBulletMat = MakeMaterial(new Color(.28f, 1f, .90f), .7f);
            enemyBulletMat = MakeMaterial(new Color(1f, .26f, .07f), .65f);
            glowMat = MakeMaterial(new Color(.12f, .90f, .66f), .7f);

            shotClip = MakeTone("shot", .085f, 190f, .33f, true);
            hitClip = MakeTone("hit", .10f, 95f, .28f, false);
            winClip = MakeTone("win", .34f, 440f, .22f, false);
        }

        void RemoveTemplateScene()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root != gameObject) Destroy(root);
        }

        void CreateCameraLightAndUI()
        {
            var c = new GameObject("RR Camera");
            DontDestroyOnLoad(c);
            cam = c.AddComponent<Camera>();
            c.AddComponent<AudioListener>();
            cam.orthographic = true;
            cam.orthographicSize = 8.25f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.018f, .027f, .045f);
            cam.transform.position = new Vector3(0, 0, -12f);
            cameraHome = cam.transform.position;

            var lightObj = new GameObject("RR Light");
            lightObj.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.7f;
            light.color = new Color(.84f, .93f, 1f);
            DontDestroyOnLoad(lightObj);

            var audioObj = new GameObject("RR Audio");
            audioObj.transform.SetParent(transform, false);
            audioSource = audioObj.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.volume = .72f;

            var canvasObj = new GameObject("RR UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasObj);
            canvas = canvasObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2400);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .55f;

            var safe = new GameObject("Safe Area", typeof(RectTransform));
            safe.transform.SetParent(canvasObj.transform, false);
            safeRoot = safe.GetComponent<RectTransform>();
            safeRoot.anchorMin = Vector2.zero; safeRoot.anchorMax = Vector2.one;
            safeRoot.offsetMin = Vector2.zero; safeRoot.offsetMax = Vector2.zero;
            ApplySafeArea();

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("RR EventSystem", typeof(EventSystem));
                var module = es.AddComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
                DontDestroyOnLoad(es);
            }
        }

        void ApplySafeArea()
        {
            Rect a = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            safeRoot.anchorMin = new Vector2(a.xMin / Screen.width, a.yMin / Screen.height);
            safeRoot.anchorMax = new Vector2(a.xMax / Screen.width, a.yMax / Screen.height);
            safeRoot.offsetMin = Vector2.zero; safeRoot.offsetMax = Vector2.zero;
        }

        void Update()
        {
            menuTime += Time.unscaledDeltaTime;
            if (menuGun != null)
            {
                float bob = Mathf.Sin(menuTime * 1.7f) * .16f;
                menuGun.position = new Vector3(0, .7f + bob, 0);
                menuGun.rotation = Quaternion.Euler(0, Mathf.Sin(menuTime * .7f) * 12f, Mathf.Sin(menuTime * .85f) * 7f);
            }

            if (CurrentMode == Mode.Playing && Player != null && !finishing && FirePressedThisFrame())
                Player.TryFire();

            if (shakeTime > 0f)
            {
                shakeTime -= Time.unscaledDeltaTime;
                cam.transform.position = cameraHome + new Vector3(Mathf.Sin(Time.unscaledTime * 71f), Mathf.Cos(Time.unscaledTime * 83f), 0) * shakeStrength;
                shakeStrength *= .90f;
            }
            else cam.transform.position = cameraHome;
        }

        bool FirePressedThisFrame()
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) return true;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
            return false;
        }

        public void ShowMenu()
        {
            finishing = false;
            CurrentMode = Mode.Menu;
            ClearWorld(); ClearUI();

            worldRoot = new GameObject("Menu World").transform;
            BuildMenuBackdrop(worldRoot);

            menuGun = new GameObject("Menu Weapon").transform;
            menuGun.SetParent(worldRoot);
            CreateGunVisual(menuGun, playerMat, playerAccentMat, 1.6f);

            CreateText(safeRoot, "RECOIL", 112, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(.08f,.79f), new Vector2(.92f,.91f), new Color(.88f,.96f,1f));
            CreateText(safeRoot, "RIVALS", 112, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(.08f,.72f), new Vector2(.92f,.84f), new Color(.18f,.95f,.82f));
            CreateText(safeRoot, "FIRE  •  FLY  •  FIGHT", 31, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(.12f,.675f), new Vector2(.88f,.73f), new Color(.48f,.59f,.68f));

            CreateBadge("LEVEL " + currentLevel, new Vector2(.04f,.925f), new Vector2(.34f,.985f));
            CreateBadge("COINS  " + coins, new Vector2(.66f,.925f), new Vector2(.96f,.985f));

            var weaponPanel = CreatePanel(safeRoot, new Vector2(.10f,.21f), new Vector2(.90f,.36f), new Color(.055f,.075f,.105f,.96f));
            CreateText(weaponPanel, "EQUIPPED", 24, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(.05f,.58f), new Vector2(.95f,.92f), new Color(.40f,.54f,.62f));
            CreateText(weaponPanel, Weapons[selectedWeapon].name, 43, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(.05f,.18f), new Vector2(.95f,.66f), new Color(.90f,.97f,1f));

            CreateButton(safeRoot, "PLAY  LEVEL " + currentLevel, new Vector2(.10f,.095f), new Vector2(.90f,.19f), new Color(.10f,.88f,.68f), new Color(.025f,.08f,.075f), () => StartLevel(currentLevel), 45);
            CreateButton(safeRoot, "WEAPONS", new Vector2(.24f,.025f), new Vector2(.76f,.082f), new Color(.10f,.14f,.19f), new Color(.72f,.84f,.90f), ShowWeapons, 30);
            CreateText(safeRoot, "One tap fires. Recoil is your movement.", 27, FontStyle.Normal, TextAnchor.MiddleCenter, new Vector2(.08f,.37f), new Vector2(.92f,.43f), new Color(.50f,.62f,.69f));
        }

        void ShowWeapons()
        {
            CurrentMode = Mode.Weapons;
            ClearUI();
            CreateText(safeRoot, "ARSENAL", 84, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(.08f,.82f), new Vector2(.92f,.93f), new Color(.90f,.97f,1f));
            CreateText(safeRoot, "Choose how you want recoil to feel.", 28, FontStyle.Normal, TextAnchor.MiddleCenter, new Vector2(.08f,.76f), new Vector2(.92f,.82f), new Color(.48f,.59f,.68f));

            float top = .66f;
            for (int i=0;i<Weapons.Length;i++)
            {
                int index=i;
                var panel=CreatePanel(safeRoot,new Vector2(.08f,top-i*.18f),new Vector2(.92f,top+.13f-i*.18f),i==selectedWeapon?new Color(.06f,.19f,.18f,.98f):new Color(.055f,.072f,.10f,.98f));
                CreateText(panel,Weapons[i].name,38,FontStyle.Bold,TextAnchor.MiddleLeft,new Vector2(.06f,.52f),new Vector2(.72f,.88f),Color.white);
                CreateText(panel,"DMG "+Weapons[i].damage.ToString("0.0")+"   RECOIL "+Weapons[i].recoil.ToString("0.0"),23,FontStyle.Normal,TextAnchor.MiddleLeft,new Vector2(.06f,.16f),new Vector2(.72f,.52f),new Color(.48f,.62f,.70f));
                CreateButton(panel,i==selectedWeapon?"EQUIPPED":"EQUIP",new Vector2(.72f,.22f),new Vector2(.95f,.78f),i==selectedWeapon?new Color(.12f,.78f,.62f):new Color(.12f,.16f,.22f),Color.white,()=>{selectedWeapon=index;PlayerPrefs.SetInt("RR_Weapon",selectedWeapon);PlayerPrefs.Save();ShowWeapons();},23);
            }
            CreateButton(safeRoot,"BACK",new Vector2(.25f,.06f),new Vector2(.75f,.13f),new Color(.10f,.14f,.19f),Color.white,ShowMenu,30);
        }

        public void StartLevel(int level)
        {
            currentLevel=Mathf.Clamp(level,1,10);
            finishing=false; CurrentMode=Mode.Playing;
            ClearWorld(); ClearUI();

            worldRoot=new GameObject("Arena Level "+currentLevel).transform;
            BuildArena(currentLevel);

            var playerRoot=new GameObject("Player Weapon");
            playerRoot.transform.SetParent(worldRoot);
            playerRoot.transform.position=new Vector3(-1.6f,-4.7f,0);
            playerRoot.transform.rotation=Quaternion.Euler(0,0,15f);
            CreateGunVisual(playerRoot.transform,playerMat,playerAccentMat,1f);
            Player=playerRoot.AddComponent<PlayerGun>();
            Player.Setup(this,(RRWeaponType)selectedWeapon);

            SpawnLevelEnemies(currentLevel);
            CreateGameplayUI();

            if(currentLevel==1) CreateTutorial("TAP ANYWHERE TO SHOOT","SHOTS PUSH YOU BACK");
            else if(currentLevel==2) CreateTutorial("USE RECOIL TO DODGE","KEEP YOUR GUN IN MOTION");
            else if(currentLevel==3) CreateTutorial("SPIN • AIM • FIRE","MASTER THE RECOIL");
        }

        void SpawnLevelEnemies(int level)
        {
            enemies.Clear();
            if(level==10)
            {
                SpawnEnemy(enemySpots[0],RREnemyType.Heavy,8f,1.35f);
                SpawnEnemy(enemySpots[1],RREnemyType.Fast,2f,.9f);
                SpawnEnemy(enemySpots[2],RREnemyType.Shotgun,3f,1f);
                return;
            }

            int count=Mathf.Clamp(1+(level-1)/2,1,4);
            for(int i=0;i<count;i++)
            {
                RREnemyType type=(RREnemyType)((level+i-1)%4);
                float hp=type==RREnemyType.Heavy?4f:(type==RREnemyType.Shotgun?3f:2f);
                if(level<=2) hp=1f;
                SpawnEnemy(enemySpots[i],type,hp,1f);
            }
        }

        void SpawnEnemy(Vector2 pos,RREnemyType type,float hp,float scale)
        {
            var root=new GameObject(type+" Enemy");
            root.transform.SetParent(worldRoot);
            root.transform.position=new Vector3(pos.x,pos.y,0);
            Material body=type==RREnemyType.Fast?enemyFastMat:type==RREnemyType.Heavy?enemyHeavyMat:type==RREnemyType.Shotgun?enemyShotgunMat:enemyMat;
            CreateGunVisual(root.transform,body,enemyBulletMat,scale);
            var unit=root.AddComponent<EnemyUnit>();
            unit.Setup(this,type,hp,scale);
            enemies.Add(unit);
        }

        void BuildArena(int level)
        {
            cam.backgroundColor=new Color(.018f,.027f,.045f);
            CreateBackdropStrips(worldRoot);
            CreateWall(new Vector2(-4.05f,0),new Vector2(.36f,15.8f));
            CreateWall(new Vector2(4.05f,0),new Vector2(.36f,15.8f));
            CreateWall(new Vector2(0,-7.25f),new Vector2(8.5f,.38f));
            CreateWall(new Vector2(0,7.25f),new Vector2(8.5f,.38f));

            if(level>=2) CreatePlatform(new Vector2(.6f,-.4f),new Vector2(3.1f,.35f),level%2==0?-13f:10f);
            if(level>=4) CreatePlatform(new Vector2(-2.35f,2.4f),new Vector2(2.4f,.32f),16f);
            if(level>=6) CreatePlatform(new Vector2(2.25f,-2.55f),new Vector2(2.2f,.32f),-18f);
            if(level>=8) CreatePlatform(new Vector2(-.15f,4.35f),new Vector2(2.7f,.32f),8f);
            if(level==10){CreatePlatform(new Vector2(0,1.45f),new Vector2(2.2f,.34f),0);CreatePlatform(new Vector2(-2.4f,-3.45f),new Vector2(1.6f,.30f),21f);}
        }

        void BuildMenuBackdrop(Transform parent)
        {
            for(int i=-4;i<=4;i++) MakeCube("Grid V",parent,new Vector3(i*1.15f,0,2f),new Vector3(.025f,13f,.04f),arenaAccentMat);
            for(int j=-5;j<=5;j++) MakeCube("Grid H",parent,new Vector3(0,j*1.2f,2f),new Vector3(8f,.025f,.04f),arenaAccentMat);
            MakeCube("Menu Base",parent,new Vector3(0,-6.7f,0),new Vector3(7.8f,.22f,.8f),arenaMat);
        }

        void CreateBackdropStrips(Transform parent)
        {
            for(int i=-3;i<=3;i++) MakeCube("Backdrop Strip",parent,new Vector3(i*1.25f,0,3f),new Vector3(.025f,14f,.03f),arenaAccentMat);
        }

        void CreateWall(Vector2 pos,Vector2 size)
        {
            var go=MakeCube("Arena Wall",worldRoot,new Vector3(pos.x,pos.y,0),new Vector3(size.x,size.y,.7f),arenaMat);
            var col=go.AddComponent<BoxCollider2D>(); col.sharedMaterial=bounceMat; go.AddComponent<ArenaWall>();
        }

        void CreatePlatform(Vector2 pos,Vector2 size,float angle)
        {
            var go=MakeCube("Platform",worldRoot,new Vector3(pos.x,pos.y,0),new Vector3(size.x,size.y,.55f),arenaAccentMat);
            go.transform.rotation=Quaternion.Euler(0,0,angle);
            var col=go.AddComponent<BoxCollider2D>(); col.sharedMaterial=bounceMat; go.AddComponent<ArenaWall>();
            var accent=MakeCube("Platform Accent",go.transform,Vector3.zero,new Vector3(.92f,.08f,.05f),glowMat);
            accent.transform.localPosition=new Vector3(0,.54f,-.32f);
        }

        public void CreateGunVisual(Transform root,Material body,Material accent,float scale)
        {
            MakeCube("Body",root,Vector3.zero,new Vector3(1.75f,.46f,.55f)*scale,body);
            var barrel=MakeCube("Barrel",root,Vector3.zero,new Vector3(.72f,.22f,.34f)*scale,accent); barrel.transform.localPosition=new Vector3(1.10f*scale,.04f*scale,-.02f);
            var grip=MakeCube("Grip",root,Vector3.zero,new Vector3(.38f,.95f,.45f)*scale,body); grip.transform.localPosition=new Vector3(-.47f*scale,-.48f*scale,.02f); grip.transform.localRotation=Quaternion.Euler(0,0,-15f);
            var sight=MakeCube("Sight",root,Vector3.zero,new Vector3(.24f,.10f,.32f)*scale,accent); sight.transform.localPosition=new Vector3(.36f*scale,.29f*scale,-.03f);
            var muzzle=MakeCube("Muzzle",root,Vector3.zero,new Vector3(.18f,.34f,.45f)*scale,body); muzzle.transform.localPosition=new Vector3(1.47f*scale,.04f*scale,0);
        }

        GameObject MakeCube(string name,Transform parent,Vector3 pos,Vector3 scale,Material mat)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name=name; go.transform.SetParent(parent,false); go.transform.position=pos; go.transform.localScale=scale;
            var r=go.GetComponent<Renderer>(); r.sharedMaterial=mat; r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows=false;
            var c=go.GetComponent<Collider>(); if(c!=null) Destroy(c);
            return go;
        }

        public void SpawnPlayerBullet(Vector2 pos,Vector2 dir,float damage,float speed,int bounces,Collider2D owner)
        { SpawnBullet(pos,dir,damage,speed,bounces,true,owner,.105f,playerBulletMat); }

        public void SpawnEnemyBullet(Vector2 pos,Vector2 dir,float damage,float speed,Collider2D owner,float size=.12f)
        { SpawnBullet(pos,dir,damage,speed,0,false,owner,size,enemyBulletMat); }

        void SpawnBullet(Vector2 pos,Vector2 dir,float damage,float speed,int bounces,bool playerOwned,Collider2D owner,float size,Material mat)
        {
            var bullet=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bullet.name=playerOwned?"Player Bullet":"Enemy Bullet";
            bullet.transform.SetParent(worldRoot); bullet.transform.position=new Vector3(pos.x,pos.y,-.2f); bullet.transform.localScale=Vector3.one*size*2f;
            var r=bullet.GetComponent<Renderer>(); r.sharedMaterial=mat; r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows=false;
            var c3=bullet.GetComponent<Collider>(); if(c3!=null) Destroy(c3);
            var rb=bullet.AddComponent<Rigidbody2D>(); rb.gravityScale=0; rb.collisionDetectionMode=CollisionDetectionMode2D.Continuous; rb.linearVelocity=dir.normalized*speed;
            var col=bullet.AddComponent<CircleCollider2D>(); col.radius=.45f; col.sharedMaterial=bounces>0?bounceMat:null;
            if(owner!=null) Physics2D.IgnoreCollision(col,owner,true);
            var trail=bullet.AddComponent<TrailRenderer>(); trail.time=.12f; trail.startWidth=size*.7f; trail.endWidth=0; trail.material=MakeTrailMaterial(playerOwned?new Color(.3f,1f,.92f):new Color(1f,.28f,.08f)); trail.minVertexDistance=.04f;
            bullet.AddComponent<RRBullet>().Setup(this,playerOwned,damage,bounces);
        }

        public void OnPlayerShot(Vector2 muzzle){Play(shotClip,.82f,1f);SpawnFlash(muzzle,playerAccentMat,.25f);Shake(.09f,.055f);}
        public void OnEnemyShot(Vector2 muzzle){Play(shotClip,.32f,.72f);SpawnFlash(muzzle,enemyBulletMat,.19f);}

        public void EnemyKilled(EnemyUnit unit)
        {
            if(unit==null)return;
            enemies.Remove(unit); coins+=8; UpdateHUD(); Play(hitClip,.75f,.78f);
            SpawnSparks(unit.transform.position,unit.Type==RREnemyType.Heavy?12:7,new Color(1f,.34f,.12f)); Shake(.16f,.10f);
            if(enemies.Count==0&&!finishing){finishing=true;StartCoroutine(VictoryRoutine());}
        }

        public void DamagePlayer(float amount)
        {
            if(Player==null||finishing||CurrentMode!=Mode.Playing)return;
            Player.Health-=Mathf.CeilToInt(amount); Player.Health=Mathf.Max(0,Player.Health); UpdateHUD();
            Play(hitClip,.85f,.58f); SpawnSparks(Player.transform.position,8,new Color(.12f,.95f,.86f)); Shake(.22f,.13f);
#if UNITY_ANDROID && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
            if(Player.Health<=0){finishing=true;StartCoroutine(DefeatRoutine());}
        }

        IEnumerator VictoryRoutine()
        {
            yield return new WaitForSecondsRealtime(.42f);
            CurrentMode=Mode.Victory;
            int completed=currentLevel, reward=45+currentLevel*7; coins+=reward;
            if(completed<10) currentLevel=completed+1;
            PlayerPrefs.SetInt("RR_Level",currentLevel);PlayerPrefs.SetInt("RR_Coins",coins);PlayerPrefs.SetInt("RR_Weapon",selectedWeapon);PlayerPrefs.Save();
            Play(winClip,.9f,1f); ShowResult(true,reward,completed);
        }

        IEnumerator DefeatRoutine(){yield return new WaitForSecondsRealtime(.35f);CurrentMode=Mode.Defeat;ShowResult(false,0,currentLevel);}

        void ShowResult(bool victory,int reward,int completed)
        {
            var dim=CreatePanel(safeRoot,Vector2.zero,Vector2.one,new Color(.012f,.018f,.028f,.91f));
            CreateText(dim,victory?"LEVEL COMPLETE":"SYSTEM DOWN",72,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.06f,.65f),new Vector2(.94f,.80f),victory?new Color(.20f,1f,.77f):new Color(1f,.30f,.24f));
            if(victory)
            {
                CreateText(dim,"+"+reward+" COINS",38,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.10f,.55f),new Vector2(.90f,.65f),Color.white);
                if(completed<10) CreateButton(dim,"NEXT LEVEL",new Vector2(.12f,.38f),new Vector2(.88f,.47f),new Color(.10f,.88f,.68f),new Color(.02f,.07f,.06f),()=>StartLevel(completed+1),38);
                else CreateText(dim,"PROTOTYPE RUN COMPLETE",30,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.12f,.45f),new Vector2(.88f,.53f),new Color(.58f,.72f,.80f));
                CreateButton(dim,"REPLAY",new Vector2(.18f,.27f),new Vector2(.82f,.35f),new Color(.10f,.14f,.19f),Color.white,()=>StartLevel(completed),31);
            }
            else
            {
                CreateText(dim,"Recoil control is everything.",28,FontStyle.Normal,TextAnchor.MiddleCenter,new Vector2(.10f,.54f),new Vector2(.90f,.63f),new Color(.58f,.68f,.74f));
                CreateButton(dim,"RETRY",new Vector2(.12f,.39f),new Vector2(.88f,.48f),new Color(.95f,.25f,.20f),Color.white,()=>StartLevel(currentLevel),38);
            }
            CreateButton(dim,"HOME",new Vector2(.24f,.16f),new Vector2(.76f,.23f),new Color(.08f,.11f,.15f),Color.white,ShowMenu,29);
        }

        void CreateGameplayUI()
        {
            hpText=CreateText(safeRoot,"♥ ♥ ♥",44,FontStyle.Bold,TextAnchor.MiddleLeft,new Vector2(.04f,.92f),new Vector2(.36f,.985f),new Color(1f,.25f,.28f));
            levelText=CreateText(safeRoot,"LEVEL "+currentLevel,35,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.34f,.92f),new Vector2(.66f,.985f),Color.white);
            enemyText=CreateText(safeRoot,"ENEMIES "+enemies.Count,28,FontStyle.Bold,TextAnchor.MiddleRight,new Vector2(.64f,.92f),new Vector2(.96f,.985f),new Color(1f,.56f,.31f));
            coinText=CreateText(safeRoot,"COINS "+coins,22,FontStyle.Bold,TextAnchor.MiddleRight,new Vector2(.70f,.875f),new Vector2(.96f,.92f),new Color(.45f,.62f,.70f));
        }

        void CreateTutorial(string a,string b)
        {
            var p=CreatePanel(safeRoot,new Vector2(.10f,.035f),new Vector2(.90f,.115f),new Color(.025f,.04f,.06f,.80f));
            CreateText(p,a,29,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.02f,.48f),new Vector2(.98f,.94f),new Color(.84f,.96f,1f));
            CreateText(p,b,20,FontStyle.Normal,TextAnchor.MiddleCenter,new Vector2(.02f,.08f),new Vector2(.98f,.50f),new Color(.42f,.59f,.67f));
        }

        public void UpdateHUD()
        {
            if(hpText!=null&&Player!=null) hpText.text=Player.Health>=3?"♥ ♥ ♥":Player.Health==2?"♥ ♥":Player.Health==1?"♥":"—";
            if(levelText!=null)levelText.text="LEVEL "+currentLevel;
            if(enemyText!=null)enemyText.text="ENEMIES "+enemies.Count;
            if(coinText!=null)coinText.text="COINS "+coins;
        }

        public void Shake(float d,float s){shakeTime=Mathf.Max(shakeTime,d);shakeStrength=Mathf.Max(shakeStrength,s);}
        public void Play(AudioClip clip,float volume,float pitch){if(audioSource==null||clip==null)return;audioSource.pitch=pitch;audioSource.PlayOneShot(clip,volume);audioSource.pitch=1f;}

        public void SpawnSparks(Vector3 pos,int count,Color color)
        {
            for(int i=0;i<count;i++)
            {
                float angle=360f/Mathf.Max(1,count)*i+(i%2)*12f;
                Vector2 dir=Quaternion.Euler(0,0,angle)*Vector2.right;
                var s=GameObject.CreatePrimitive(PrimitiveType.Cube);s.name="Impact Spark";s.transform.SetParent(worldRoot);s.transform.position=new Vector3(pos.x,pos.y,-.4f);s.transform.localScale=new Vector3(.08f,.22f,.08f);
                s.GetComponent<Renderer>().material=MakeMaterial(color,.8f);var c=s.GetComponent<Collider>();if(c!=null)Destroy(c);
                var fx=s.AddComponent<RRSpark>();fx.velocity=dir*(2.3f+(i%3)*.55f);fx.life=.30f+(i%2)*.08f;
            }
        }

        void SpawnFlash(Vector2 pos,Material mat,float scale)
        {
            var f=GameObject.CreatePrimitive(PrimitiveType.Sphere);f.name="Muzzle Flash";f.transform.SetParent(worldRoot);f.transform.position=new Vector3(pos.x,pos.y,-.5f);f.transform.localScale=Vector3.one*scale;
            f.GetComponent<Renderer>().sharedMaterial=mat;var c=f.GetComponent<Collider>();if(c!=null)Destroy(c);f.AddComponent<RRFlash>();
        }

        void ClearWorld(){Player=null;menuGun=null;enemies.Clear();if(worldRoot!=null)Destroy(worldRoot.gameObject);worldRoot=null;}
        void ClearUI(){hpText=null;levelText=null;enemyText=null;coinText=null;for(int i=safeRoot.childCount-1;i>=0;i--)Destroy(safeRoot.GetChild(i).gameObject);}

        RectTransform CreatePanel(Transform parent,Vector2 min,Vector2 max,Color color)
        {
            var go=new GameObject("Panel",typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);
            var rt=go.GetComponent<RectTransform>();rt.anchorMin=min;rt.anchorMax=max;rt.offsetMin=Vector2.zero;rt.offsetMax=Vector2.zero;
            var img=go.GetComponent<Image>();img.sprite=roundedSprite;img.color=color;return rt;
        }

        Text CreateText(Transform parent,string content,int size,FontStyle style,TextAnchor anchor,Vector2 min,Vector2 max,Color color)
        {
            var go=new GameObject("Text",typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);
            var rt=go.GetComponent<RectTransform>();rt.anchorMin=min;rt.anchorMax=max;rt.offsetMin=Vector2.zero;rt.offsetMax=Vector2.zero;
            var t=go.GetComponent<Text>();t.font=font;t.text=content;t.fontSize=size;t.fontStyle=style;t.alignment=anchor;t.color=color;t.resizeTextForBestFit=true;t.resizeTextMinSize=Mathf.Max(14,size/2);t.resizeTextMaxSize=size;t.raycastTarget=false;
            var o=go.AddComponent<Outline>();o.effectColor=new Color(0,0,0,.38f);o.effectDistance=new Vector2(2,-2);return t;
        }

        Button CreateButton(Transform parent,string label,Vector2 min,Vector2 max,Color bg,Color textColor,Action action,int fontSize)
        {
            var panel=CreatePanel(parent,min,max,bg);panel.gameObject.name="Button "+label;
            var button=panel.gameObject.AddComponent<Button>();button.onClick.AddListener(()=>action());
            CreateText(panel,label,fontSize,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.03f,.08f),new Vector2(.97f,.92f),textColor);return button;
        }

        void CreateBadge(string text,Vector2 min,Vector2 max)
        {
            var p=CreatePanel(safeRoot,min,max,new Color(.045f,.07f,.095f,.92f));
            CreateText(p,text,26,FontStyle.Bold,TextAnchor.MiddleCenter,new Vector2(.05f,.10f),new Vector2(.95f,.90f),new Color(.72f,.86f,.91f));
        }

        Sprite CreateRoundedSprite()
        {
            const int s=64;const float radius=13f;
            var tex=new Texture2D(s,s,TextureFormat.RGBA32,false);tex.wrapMode=TextureWrapMode.Clamp;tex.filterMode=FilterMode.Bilinear;
            var px=new Color32[s*s];
            for(int y=0;y<s;y++)for(int x=0;x<s;x++){float dx=Mathf.Max(radius-x,x-(s-1-radius));float dy=Mathf.Max(radius-y,y-(s-1-radius));float outside=Mathf.Sqrt(Mathf.Max(0,dx)*Mathf.Max(0,dx)+Mathf.Max(0,dy)*Mathf.Max(0,dy))-radius;byte a=(byte)Mathf.Clamp(Mathf.RoundToInt((1f-Mathf.Clamp01(outside+.4f))*255f),0,255);px[y*s+x]=new Color32(255,255,255,a);}
            tex.SetPixels32(px);tex.Apply();return Sprite.Create(tex,new Rect(0,0,s,s),new Vector2(.5f,.5f),100f);
        }

        Material MakeMaterial(Color color,float emission)
        {
            Shader shader=Shader.Find("Universal Render Pipeline/Lit");if(shader==null)shader=Shader.Find("Standard");
            var m=new Material(shader);if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color);if(m.HasProperty("_Color"))m.SetColor("_Color",color);
            if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.58f);if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",.28f);
            if(emission>0&&m.HasProperty("_EmissionColor")){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*emission);}return m;
        }

        Material MakeTrailMaterial(Color color)
        {
            Shader shader=Shader.Find("Universal Render Pipeline/Unlit");if(shader==null)shader=Shader.Find("Sprites/Default");
            var m=new Material(shader);if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color);if(m.HasProperty("_Color"))m.SetColor("_Color",color);return m;
        }

        AudioClip MakeTone(string name,float seconds,float frequency,float volume,bool noisy)
        {
            int sr=22050,count=Mathf.Max(64,Mathf.RoundToInt(sr*seconds));var data=new float[count];var rng=new System.Random(1729+name.GetHashCode());
            for(int i=0;i<count;i++){float t=i/(float)sr,env=Mathf.Pow(1f-i/(float)count,2.2f),sine=Mathf.Sin(2f*Mathf.PI*frequency*t),noise=noisy?((float)rng.NextDouble()*2f-1f)*.55f:0;data[i]=(sine*.65f+noise)*env*volume;}
            var clip=AudioClip.Create(name,count,1,sr,false);clip.SetData(data,0);return clip;
        }
    }

    public class PlayerGun : MonoBehaviour
    {
        public int Health=3;
        RecoilRivalsGame game; RRWeaponType weaponType; Rigidbody2D rb; BoxCollider2D col; float nextFire;

        public void Setup(RecoilRivalsGame game,RRWeaponType weaponType)
        {
            this.game=game;this.weaponType=weaponType;
            rb=gameObject.AddComponent<Rigidbody2D>();rb.gravityScale=.20f;rb.collisionDetectionMode=CollisionDetectionMode2D.Continuous;rb.interpolation=RigidbodyInterpolation2D.Interpolate;rb.linearDamping=.38f;rb.angularDamping=.34f;
            col=gameObject.AddComponent<BoxCollider2D>();col.size=new Vector2(2f,1.18f);game.UpdateHUD();
        }

        public void TryFire()
        {
            if(Time.unscaledTime<nextFire||game.CurrentMode!=RecoilRivalsGame.Mode.Playing)return;
            RRWeaponStats s=game.Weapons[(int)weaponType];nextFire=Time.unscaledTime+s.fireDelay;
            Vector2 dir=transform.right,muzzle=(Vector2)transform.position+dir*1.62f;int pellets=Mathf.Max(1,s.pellets);
            for(int i=0;i<pellets;i++){float off=pellets==1?0:Mathf.Lerp(-s.spread,s.spread,i/(float)(pellets-1));Vector2 shot=Quaternion.Euler(0,0,off)*dir;game.SpawnPlayerBullet(muzzle+shot*.12f,shot,s.damage,s.bulletSpeed,s.bounces,col);}
            rb.AddForce(-dir*s.recoil,ForceMode2D.Impulse);rb.AddTorque(s.torque,ForceMode2D.Impulse);game.OnPlayerShot(muzzle);
        }

        void FixedUpdate(){if(rb==null)return;if(rb.linearVelocity.magnitude>9.5f)rb.linearVelocity=rb.linearVelocity.normalized*9.5f;rb.angularVelocity=Mathf.Clamp(rb.angularVelocity,-240f,240f);}
    }

    public class EnemyUnit : MonoBehaviour
    {
        public RREnemyType Type{get;private set;}
        RecoilRivalsGame game;float health,scale,nextShot,fireInterval,projectileSpeed,projectileDamage;BoxCollider2D col;

        public void Setup(RecoilRivalsGame game,RREnemyType type,float hp,float scale)
        {
            this.game=game;Type=type;health=hp;this.scale=scale;col=gameObject.AddComponent<BoxCollider2D>();col.size=new Vector2(2.05f*scale,1.15f*scale);
            if(type==RREnemyType.Fast){fireInterval=.82f;projectileSpeed=11.5f;projectileDamage=1;}
            else if(type==RREnemyType.Heavy){fireInterval=2.15f;projectileSpeed=8f;projectileDamage=1.25f;}
            else if(type==RREnemyType.Shotgun){fireInterval=1.75f;projectileSpeed=9.5f;projectileDamage=1;}
            else{fireInterval=1.40f;projectileSpeed=10f;projectileDamage=1;}
            nextShot=Time.time+.9f+(int)type*.16f;
        }

        void Update()
        {
            if(game==null||game.CurrentMode!=RecoilRivalsGame.Mode.Playing||game.Player==null)return;
            Vector2 delta=game.Player.transform.position-transform.position;if(delta.sqrMagnitude<.01f)return;
            float wanted=Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg;
            transform.rotation=Quaternion.Euler(0,0,Mathf.LerpAngle(transform.eulerAngles.z,wanted,Time.deltaTime*(Type==RREnemyType.Fast?4f:2.7f)));
            if(Time.time>=nextShot){nextShot=Time.time+fireInterval;Fire(delta.normalized);}
        }

        void Fire(Vector2 dir)
        {
            Vector2 muzzle=(Vector2)transform.position+dir*(1.58f*scale);
            if(Type==RREnemyType.Shotgun){for(int i=0;i<4;i++){float a=Mathf.Lerp(-12f,12f,i/3f);game.SpawnEnemyBullet(muzzle,Quaternion.Euler(0,0,a)*dir,projectileDamage,projectileSpeed,col,.09f);}}
            else game.SpawnEnemyBullet(muzzle,dir,projectileDamage,projectileSpeed,col,Type==RREnemyType.Heavy?.18f:.115f);
            game.OnEnemyShot(muzzle);
        }

        public void TakeDamage(float damage){health-=damage;if(health<=0){game.EnemyKilled(this);Destroy(gameObject);}else game.SpawnSparks(transform.position,4,new Color(1f,.46f,.14f));}
    }

    public class RRBullet : MonoBehaviour
    {
        RecoilRivalsGame game;bool playerOwned;float damage;int bounces;float deathAt;
        public void Setup(RecoilRivalsGame game,bool playerOwned,float damage,int bounces){this.game=game;this.playerOwned=playerOwned;this.damage=damage;this.bounces=bounces;deathAt=Time.time+4.5f;}
        void Update(){if(Time.time>=deathAt)Destroy(gameObject);}
        void OnCollisionEnter2D(Collision2D c)
        {
            if(playerOwned){var e=c.collider.GetComponent<EnemyUnit>();if(e!=null){e.TakeDamage(damage);Destroy(gameObject);return;}}
            else{var p=c.collider.GetComponent<PlayerGun>();if(p!=null){game.DamagePlayer(damage);Destroy(gameObject);return;}}
            if(c.collider.GetComponent<ArenaWall>()!=null){if(bounces>0){bounces--;game.SpawnSparks(transform.position,3,playerOwned?new Color(.25f,1f,.9f):new Color(1f,.3f,.1f));}else Destroy(gameObject);}
        }
    }

    public class ArenaWall:MonoBehaviour{}
    public class RRSpark:MonoBehaviour
    {
        public Vector2 velocity;public float life=.32f;float age;
        void Update(){age+=Time.deltaTime;transform.position+=(Vector3)(velocity*Time.deltaTime);velocity*=.94f;transform.Rotate(0,0,420f*Time.deltaTime);float k=Mathf.Clamp01(1f-age/life);transform.localScale=new Vector3(.07f*k,.20f*k,.07f*k);if(age>=life)Destroy(gameObject);}
    }
    public class RRFlash:MonoBehaviour{float age;void Update(){age+=Time.deltaTime;transform.localScale*=1f+Time.deltaTime*6f;if(age>.075f)Destroy(gameObject);}}
}
