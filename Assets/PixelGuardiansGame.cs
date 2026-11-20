using UnityEngine;
using System.Collections.Generic;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class PixelGuardiansGame : MonoBehaviour
{
    [Header("=== KEO THA ANH TU THU MUC 'frames' VAO DAY ===")]
    [Header("1. Nhan vat & Vu khi")]
    public Sprite spWarrior;
    public Sprite spMage;
    public Sprite spRanger;
    public Sprite spWeaponSword;
    public Sprite spWeaponStaff;
    public Sprite spWeaponBow;

    [Header("2. Quai vat & Boss")]
    public Sprite spEnemyFast;
    public Sprite spEnemyTank;
    public Sprite spEnemyRanged;
    public Sprite spEnemyBomber;
    public Sprite spBoss;

    [Header("3. Vat pham rot ra")]
    public Sprite spGold;
    public Sprite spHpPot;
    public Sprite spMpPot;

    [Header("4. Cai dat Ban do rong")]
    public float mapHalfWidth = 15f;
    public float mapHalfHeight = 12f;

    // --- TRẠNG THÁI GAME ---
    enum GameState { ClassSelect, Playing, Shop, GameOver, Victory }
    enum HeroClass { Warrior, Mage, Ranger }
    enum EnemyType { Fast, Tank, Ranged, Bomber, Boss }
    enum DropType { Gold, HPPotion, MPPotion }

    GameState state = GameState.ClassSelect;
    HeroClass selectedClass = HeroClass.Warrior;

    // --- THÔNG SỐ NGƯỜI CHƠI ---
    GameObject playerObj, weaponObj;
    Rigidbody2D playerRb;
    Vector2 playerPos;
    float hp, maxHp, mp, maxMp, atk, spd;
    int gold = 0, exp = 0, level = 1, weaponLv = 1;
    bool hasThornArmor = false, hasLifesteal = false;

    float attackTimer = 0f, attackCooldown = 0.4f;
    float skillTimer = 0f, skillCooldown = 4f;
    float dashTimer = 0f, dashDuration = 0f;
    Vector2 dashDir;
    float invulnTimer = 0f;
    float animTime = 0f;

    // --- QUẢN LÝ WAVE ---
    int currentWave = 1, maxWaves = 5, enemiesToSpawn = 0;
    float spawnTimer = 0f;

    class Enemy
    {
        public GameObject obj, hpBarBg, hpBarFill;
        public Vector2 pos;
        public EnemyType type;
        public float hp, maxHp, spd, dmg, radius, shootTimer, slowTimer, baseScale;
    }

    class Projectile
    {
        public GameObject obj;
        public Vector2 pos, vel;
        public float dmg, life, radius;
        public bool fromPlayer, isAoe;
    }

    class DropItem
    {
        public GameObject obj;
        public Vector2 pos;
        public DropType type;
    }

    class VisualEffect
    {
        public GameObject obj;
        public float life, maxLife, startScale, endScale;
    }

    List<Enemy> enemies = new List<Enemy>();
    List<Projectile> projectiles = new List<Projectile>();
    List<DropItem> drops = new List<DropItem>();
    List<VisualEffect> effects = new List<VisualEffect>();

    Sprite spBulletPlayer, spBulletEnemy, spCircle, spSquare;
    Camera cam;
    Texture2D uiBgTex, uiBarTex;
    string topSortingLayer = "Default";

    void Start()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
        cam.orthographic = true;
        cam.orthographicSize = 6.5f;
        foreach (var c in FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
            if (c.gameObject != playerObj) c.enabled = false;
        if (SortingLayer.layers.Length > 0)
            topSortingLayer = SortingLayer.layers[SortingLayer.layers.Length - 1].name;

        EnsureFallbackSprites();
        uiBgTex = MakeSolidTex(new Color(0.08f, 0.09f, 0.14f, 0.92f));
        uiBarTex = MakeSolidTex(Color.white);
    }

    void Update()
    {
        if (state != GameState.Playing)
        {
            if (playerRb != null) playerRb.linearVelocity = Vector2.zero;
            return;
        }

        float dt = Time.deltaTime;
        animTime += dt;

        UpdatePlayer(dt);
        UpdateCameraFollow(dt);
        UpdateSpawner(dt);
        UpdateEnemies(dt);
        UpdateProjectiles(dt);
        UpdateDrops();
        UpdateEffects(dt);

        if (enemiesToSpawn <= 0 && enemies.Count == 0)
        {
            if (playerRb != null) playerRb.linearVelocity = Vector2.zero;
            state = (currentWave >= maxWaves) ? GameState.Victory : GameState.Shop;
        }
    }

    void UpdateCameraFollow(float dt)
    {
        Vector3 target = new Vector3(playerPos.x, playerPos.y, -10f);
        cam.transform.position = Vector3.Lerp(cam.transform.position, target, 8f * dt);
    }

    // ==================== HỆ THỐNG VA CHẠM ĐỊA HÌNH THEO TẦNG ====================
    bool IsBlockedOnCurrentLayer(Vector2 pos, float radius)
    {
        if (pos.x < -mapHalfWidth || pos.x > mapHalfWidth || pos.y < -mapHalfHeight || pos.y > mapHalfHeight)
            return true;

        int mask = (playerObj != null) ? (1 << playerObj.layer) : Physics2D.DefaultRaycastLayers;
        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, radius, mask);
        foreach (var h in hits)
        {
            if (h != null && !h.isTrigger && h.gameObject != playerObj) return true;
        }
        return false;
    }

    Vector2 MoveEnemyWithCollision(Vector2 currentPos, Vector2 delta, float radius)
    {
        Vector2 nextPos = currentPos;
        Vector2 tryX = new Vector2(currentPos.x + delta.x, currentPos.y);
        if (!IsBlockedOnCurrentLayer(tryX, radius)) nextPos.x = tryX.x;

        Vector2 tryY = new Vector2(nextPos.x, currentPos.y + delta.y);
        if (!IsBlockedOnCurrentLayer(tryY, radius)) nextPos.y = tryY.y;

        return nextPos;
    }

    // ==================== KHỞI TẠO GAME & WAVE ====================
    void StartNewGame(HeroClass hc)
    {
        ClearAllEntities();
        selectedClass = hc;
        gold = 0; exp = 0; level = 1; weaponLv = 1;
        hasThornArmor = false; hasLifesteal = false;
        currentWave = 1;
        playerPos = Vector2.zero;

        if (hc == HeroClass.Warrior) { maxHp = 180; maxMp = 80; atk = 28; spd = 5.5f; attackCooldown = 0.42f; }
        else if (hc == HeroClass.Mage) { maxHp = 100; maxMp = 140; atk = 24; spd = 5.2f; attackCooldown = 0.55f; }
        else { maxHp = 125; maxMp = 100; atk = 18; spd = 6.5f; attackCooldown = 0.25f; }

        hp = maxHp; mp = maxMp;
        Sprite heroSp = hc == HeroClass.Warrior ? spWarrior : (hc == HeroClass.Mage ? spMage : spRanger);
        playerObj = CreateObj("Player", heroSp, 1.0f, 55);
        playerObj.transform.position = playerPos;

        // Gắn Rigidbody2D + Collider nhỏ ở chân để tương thích 100% với cầu thang & tường của map Cainos
        int layer1 = LayerMask.NameToLayer("Layer 1");
        if (layer1 >= 0) playerObj.layer = layer1;

        playerRb = playerObj.AddComponent<Rigidbody2D>();
        playerRb.gravityScale = 0f;
        playerRb.freezeRotation = true;
        playerRb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        CircleCollider2D col = playerObj.AddComponent<CircleCollider2D>();
        col.radius = 0.22f;
        col.offset = new Vector2(0f, -0.25f);

        Sprite wepSp = hc == HeroClass.Warrior ? spWeaponSword : (hc == HeroClass.Mage ? spWeaponStaff : spWeaponBow);
        weaponObj = CreateObj("Weapon", wepSp, 0.9f, 56);
        weaponObj.transform.position = playerPos;

        StartWave(1);
        state = GameState.Playing;
    }

    void StartWave(int wave)
    {
        currentWave = wave;
        enemiesToSpawn = (wave == maxWaves) ? 12 : (6 + wave * 4);
        spawnTimer = 0.5f;
        if (wave == maxWaves) SpawnEnemy(EnemyType.Boss);
    }

    // ==================== ĐIỀU KHIỂN NGƯỜI CHƠI ====================
    void UpdatePlayer(float dt)
    {
        mp = Mathf.Min(maxMp, mp + 7f * dt);
        if (attackTimer > 0) attackTimer -= dt;
        if (skillTimer > 0) skillTimer -= dt;
        if (dashTimer > 0) dashTimer -= dt;
        if (invulnTimer > 0) invulnTimer -= dt;

        Vector2 moveInput = GetMoveInput();

        if (GetDashPressed() && dashTimer <= 0 && moveInput.sqrMagnitude > 0.01f)
        {
            dashDir = moveInput.normalized;
            dashDuration = 0.18f;
            dashTimer = (selectedClass == HeroClass.Warrior) ? 1.1f : 1.5f;
            invulnTimer = 0.25f;
            SpawnEffect(playerPos, new Color(0.5f, 0.9f, 1f, 0.7f), 0.5f, 1.5f, 0.2f);
        }

        if (dashDuration > 0)
        {
            dashDuration -= dt;
            playerRb.linearVelocity = dashDir * (spd * 2.8f);
        }
        else
        {
            playerRb.linearVelocity = moveInput.normalized * spd;
        }

        // Cập nhật vị trí thực tế sau khi Unity xử lý va chạm tường/cầu thang
        playerPos = playerObj.transform.position;
        if (Mathf.Abs(playerPos.x) > mapHalfWidth || Mathf.Abs(playerPos.y) > mapHalfHeight)
        {
            playerPos.x = Mathf.Clamp(playerPos.x, -mapHalfWidth, mapHalfWidth);
            playerPos.y = Mathf.Clamp(playerPos.y, -mapHalfHeight, mapHalfHeight);
            playerObj.transform.position = playerPos;
        }

        // Giữ nhân vật luôn nổi lên trên mặt đất
        var psr = playerObj.GetComponent<SpriteRenderer>();
        if (psr != null) psr.sortingOrder = 55;

        Vector2 mouseWorld = GetMouseWorldPos();
        Vector2 aimDir = (mouseWorld - playerPos).normalized;
        playerObj.transform.localScale = new Vector3(aimDir.x < 0 ? -1f : 1f, 1f, 1f);

        float angle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;
        weaponObj.transform.position = playerPos + aimDir * 0.65f;
        weaponObj.transform.rotation = Quaternion.Euler(0, 0, angle - 45f);
        if (psr != null) weaponObj.GetComponent<SpriteRenderer>().sortingLayerName = psr.sortingLayerName;

        if (GetAttackHeld() && attackTimer <= 0)
        {
            PerformBasicAttack(aimDir);
            attackTimer = attackCooldown;
        }

        if (GetSkillPressed() && skillTimer <= 0 && mp >= 25f)
        {
            mp -= 25f;
            skillTimer = skillCooldown;
            PerformSpecialSkill(mouseWorld, aimDir);
        }
    }

    void PerformBasicAttack(Vector2 dir)
    {
        if (selectedClass == HeroClass.Warrior)
        {
            Vector2 hitCenter = playerPos + dir * 1.25f;
            SpawnEffect(hitCenter, new Color(1f, 0.9f, 0.4f, 0.75f), 1.4f, 2.3f, 0.15f);
            foreach (var e in enemies.ToArray())
                if (Vector2.Distance(hitCenter, e.pos) <= 1.75f + e.radius) DamageEnemy(e, atk);
        }
        else if (selectedClass == HeroClass.Mage)
        {
            SpawnProjectile(playerPos + dir * 0.7f, dir * 10.5f, atk, 1.8f, 0.35f, true, true);
        }
        else
        {
            SpawnProjectile(playerPos + dir * 0.7f, dir * 16f, atk, 1.4f, 0.22f, true, false);
        }
    }

    void PerformSpecialSkill(Vector2 mouseWorld, Vector2 aimDir)
    {
        if (selectedClass == HeroClass.Warrior)
        {
            SpawnEffect(playerPos, new Color(1f, 0.5f, 0.2f, 0.8f), 1.5f, 4.8f, 0.35f);
            foreach (var e in enemies.ToArray())
                if (Vector2.Distance(playerPos, e.pos) <= 3.8f + e.radius) DamageEnemy(e, atk * 2.5f);
        }
        else if (selectedClass == HeroClass.Mage)
        {
            SpawnEffect(mouseWorld, new Color(0.3f, 0.7f, 1f, 0.85f), 2.0f, 5.2f, 0.5f);
            foreach (var e in enemies.ToArray())
                if (Vector2.Distance(mouseWorld, e.pos) <= 3.8f + e.radius) { e.slowTimer = 3.5f; DamageEnemy(e, atk * 2.8f); }
        }
        else
        {
            float baseAngle = Mathf.Atan2(aimDir.y, aimDir.x);
            for (int i = -4; i <= 4; i++)
            {
                float a = baseAngle + i * 0.12f;
                SpawnProjectile(playerPos, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 15.5f, atk * 1.2f, 1.3f, 0.22f, true, false);
            }
        }
    }

    // ==================== QUÁI VẬT & SPAWN ====================
    void UpdateSpawner(float dt)
    {
        if (enemiesToSpawn <= 0) return;
        spawnTimer -= dt;
        if (spawnTimer <= 0)
        {
            enemiesToSpawn--;
            spawnTimer = Mathf.Max(0.45f, 1.4f - currentWave * 0.16f);

            float r = Random.value;
            EnemyType t = EnemyType.Fast;
            if (currentWave >= 2 && r > 0.55f) t = EnemyType.Tank;
            if (currentWave >= 2 && r > 0.75f) t = EnemyType.Ranged;
            if (currentWave >= 3 && r > 0.88f) t = EnemyType.Bomber;
            SpawnEnemy(t);
        }
    }

    void SpawnEnemy(EnemyType type)
    {
        Vector2 pos = playerPos;
        for (int tries = 0; tries < 15; tries++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 candidate = playerPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(6.5f, 10f);
            if (!IsBlockedOnCurrentLayer(candidate, 0.45f)) { pos = candidate; break; }
        }

        SpawnEffect(pos, new Color(0.5f, 0f, 0.8f, 0.9f), 0.4f, 2.2f, 0.4f);

        float scaleMult = 1f + (currentWave - 1) * 0.22f;
        Enemy e = new Enemy { pos = pos, type = type, shootTimer = 1.5f, baseScale = 1f };
        Sprite sp = spEnemyFast;

        switch (type)
        {
            case EnemyType.Fast: e.maxHp = 35 * scaleMult; e.spd = 3.8f; e.dmg = 10 * scaleMult; e.radius = 0.45f; sp = spEnemyFast; break;
            case EnemyType.Tank: e.maxHp = 110 * scaleMult; e.spd = 1.8f; e.dmg = 22 * scaleMult; e.radius = 0.65f; sp = spEnemyTank; e.baseScale = 1.3f; break;
            case EnemyType.Ranged: e.maxHp = 55 * scaleMult; e.spd = 2.4f; e.dmg = 14 * scaleMult; e.radius = 0.5f; sp = spEnemyRanged; break;
            case EnemyType.Bomber: e.maxHp = 40 * scaleMult; e.spd = 4.2f; e.dmg = 35 * scaleMult; e.radius = 0.5f; sp = spEnemyBomber; break;
            case EnemyType.Boss: e.maxHp = 900; e.spd = 2.2f; e.dmg = 30; e.radius = 1.2f; sp = spBoss; e.baseScale = 2.2f; break;
        }
        e.hp = e.maxHp;
        e.obj = CreateObj("Enemy_" + type, sp, e.baseScale, 54);
        e.obj.transform.position = pos;

        e.hpBarBg = CreateObj("HPBg", spSquare, 1f, 58);
        e.hpBarBg.GetComponent<SpriteRenderer>().color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        e.hpBarBg.transform.localScale = new Vector3(0.9f * e.baseScale, 0.12f, 1f);

        e.hpBarFill = CreateObj("HPFill", spSquare, 1f, 59);
        e.hpBarFill.GetComponent<SpriteRenderer>().color = Color.red;
        e.hpBarFill.transform.localScale = new Vector3(0.9f * e.baseScale, 0.12f, 1f);

        enemies.Add(e);
    }

    void UpdateEnemies(float dt)
    {
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            Enemy e = enemies[i];
            Vector2 toPlayer = playerPos - e.pos;
            float dist = toPlayer.magnitude;
            Vector2 dir = dist > 0.01f ? toPlayer / dist : Vector2.zero;

            float curSpd = e.spd * (e.slowTimer > 0 ? 0.45f : 1f);
            if (e.slowTimer > 0) e.slowTimer -= dt;

            if (!(e.type == EnemyType.Ranged && dist < 5.5f))
                e.pos = MoveEnemyWithCollision(e.pos, dir * curSpd * dt, 0.25f);

            float bob = Mathf.Abs(Mathf.Sin(animTime * 10f + i)) * 0.08f;
            e.obj.transform.position = new Vector3(e.pos.x, e.pos.y + bob, 0);
            e.obj.transform.localScale = new Vector3(dir.x < 0 ? -e.baseScale : e.baseScale, e.baseScale, 1f);

            Vector3 barPos = new Vector3(e.pos.x, e.pos.y + 0.75f * e.baseScale, 0);
            e.hpBarBg.transform.position = barPos;
            float pct = Mathf.Clamp01(e.hp / e.maxHp);
            e.hpBarFill.transform.position = barPos;
            e.hpBarFill.transform.localScale = new Vector3(0.9f * e.baseScale * pct, 0.12f, 1f);

            if (e.type == EnemyType.Ranged || e.type == EnemyType.Boss)
            {
                e.shootTimer -= dt;
                if (e.shootTimer <= 0)
                {
                    if (e.type == EnemyType.Ranged)
                    {
                        e.shootTimer = 2.0f;
                        SpawnProjectile(e.pos, dir * 7.5f, e.dmg, 2.5f, 0.25f, false, false);
                    }
                    else
                    {
                        e.shootTimer = 2.2f;
                        for (int k = 0; k < 12; k++)
                        {
                            float a = k * (Mathf.PI * 2f / 12f);
                            SpawnProjectile(e.pos, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 6.5f, e.dmg * 0.7f, 3.0f, 0.3f, false, false);
                        }
                    }
                }
            }

            if (dist < e.radius + 0.45f)
            {
                if (e.type == EnemyType.Bomber)
                {
                    SpawnEffect(e.pos, new Color(1f, 0.4f, 0.1f, 0.9f), 1.0f, 3.2f, 0.3f);
                    DamagePlayer(e.dmg, e);
                    DestroyEnemyObj(e);
                    enemies.RemoveAt(i);
                    continue;
                }
                else
                {
                    DamagePlayer(e.dmg * dt * 1.5f, e);
                }
            }
        }
    }

    void DestroyEnemyObj(Enemy e)
    {
        if (e.obj) Destroy(e.obj);
        if (e.hpBarBg) Destroy(e.hpBarBg);
        if (e.hpBarFill) Destroy(e.hpBarFill);
    }

    void DamageEnemy(Enemy e, float amount)
    {
        if (e == null || e.obj == null) return;
        e.hp -= amount;
        SpawnEffect(e.pos, Color.white, 0.25f, 0.65f, 0.1f);
        if (hasLifesteal) hp = Mathf.Min(maxHp, hp + amount * 0.08f);

        if (e.hp <= 0)
        {
            gold += (e.type == EnemyType.Boss) ? 200 : Random.Range(8, 16);
            exp += (e.type == EnemyType.Boss) ? 100 : 20;
            if (exp >= level * 100) { exp -= level * 100; level++; maxHp += 15; hp = maxHp; atk += 4; }

            float r = Random.value;
            if (r < 0.18f) SpawnDrop(e.pos, DropType.HPPotion);
            else if (r < 0.30f) SpawnDrop(e.pos, DropType.MPPotion);
            else if (r < 0.65f) SpawnDrop(e.pos, DropType.Gold);

            DestroyEnemyObj(e);
            enemies.Remove(e);
        }
    }

    void DamagePlayer(float amount, Enemy attacker = null)
    {
        if (invulnTimer > 0) return;
        hp -= amount;
        if (hasThornArmor && attacker != null && attacker.hp > 0) DamageEnemy(attacker, amount * 0.6f);
        if (hp <= 0) { hp = 0; state = GameState.GameOver; }
    }

    // ==================== ĐẠN, VẬT PHẨM & HIỆU ỨNG ====================
    void SpawnProjectile(Vector2 pos, Vector2 vel, float dmg, float life, float radius, bool fromPlayer, bool isAoe)
    {
        Projectile p = new Projectile
        {
            pos = pos,
            vel = vel,
            dmg = dmg,
            life = life,
            radius = radius,
            fromPlayer = fromPlayer,
            isAoe = isAoe,
            obj = CreateObj("Proj", fromPlayer ? spBulletPlayer : spBulletEnemy, isAoe ? 0.7f : 0.45f, 57)
        };
        p.obj.transform.position = pos;
        projectiles.Add(p);
    }

    void UpdateProjectiles(float dt)
    {
        for (int i = projectiles.Count - 1; i >= 0; i--)
        {
            Projectile p = projectiles[i];
            p.pos += p.vel * dt; p.life -= dt;
            p.obj.transform.position = p.pos;

            bool hit = IsBlockedOnCurrentLayer(p.pos, 0.12f);
            if (hit) SpawnEffect(p.pos, Color.white, 0.15f, 0.45f, 0.12f);

            if (!hit && p.fromPlayer)
            {
                for (int j = enemies.Count - 1; j >= 0; j--)
                {
                    if (Vector2.Distance(p.pos, enemies[j].pos) <= p.radius + enemies[j].radius)
                    {
                        if (p.isAoe)
                        {
                            SpawnEffect(p.pos, new Color(0.6f, 0.3f, 1f, 0.8f), 0.8f, 2.5f, 0.25f);
                            foreach (var e in enemies.ToArray())
                                if (Vector2.Distance(p.pos, e.pos) <= 2.1f + e.radius) DamageEnemy(e, p.dmg);
                        }
                        else DamageEnemy(enemies[j], p.dmg);
                        hit = true; break;
                    }
                }
            }
            else if (!hit && Vector2.Distance(p.pos, playerPos) <= p.radius + 0.4f) { DamagePlayer(p.dmg); hit = true; }

            if (hit || p.life <= 0) { Destroy(p.obj); projectiles.RemoveAt(i); }
        }
    }

    void SpawnDrop(Vector2 pos, DropType type)
    {
        Sprite sp = type == DropType.Gold ? spGold : (type == DropType.HPPotion ? spHpPot : spMpPot);
        DropItem d = new DropItem { pos = pos, type = type, obj = CreateObj("Drop", sp, 0.8f, 52) };
        d.obj.transform.position = pos;
        drops.Add(d);
    }

    void UpdateDrops()
    {
        for (int i = drops.Count - 1; i >= 0; i--)
        {
            if (Vector2.Distance(playerPos, drops[i].pos) <= 1.1f)
            {
                if (drops[i].type == DropType.Gold) gold += 20;
                else if (drops[i].type == DropType.HPPotion) hp = Mathf.Min(maxHp, hp + maxHp * 0.35f);
                else if (drops[i].type == DropType.MPPotion) mp = Mathf.Min(maxMp, mp + maxMp * 0.45f);
                Destroy(drops[i].obj); drops.RemoveAt(i);
            }
        }
    }

    void SpawnEffect(Vector2 pos, Color col, float startS, float endS, float duration)
    {
        GameObject o = CreateObj("FX", spCircle, startS, 60);
        o.transform.position = pos;
        o.GetComponent<SpriteRenderer>().color = col;
        effects.Add(new VisualEffect { obj = o, life = duration, maxLife = duration, startScale = startS, endScale = endS });
    }

    void UpdateEffects(float dt)
    {
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            var fx = effects[i]; fx.life -= dt;
            if (fx.life <= 0) { Destroy(fx.obj); effects.RemoveAt(i); continue; }
            float t = 1f - (fx.life / fx.maxLife);
            fx.obj.transform.localScale = Vector3.one * Mathf.Lerp(fx.startScale, fx.endScale, t);
            var sr = fx.obj.GetComponent<SpriteRenderer>();
            Color c = sr.color; c.a = 1f - t; sr.color = c;
        }
    }

    // ==================== GIAO DIỆN (UI) ====================
    void OnGUI()
    {
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        titleStyle.normal.textColor = Color.yellow;
        GUIStyle textStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
        textStyle.normal.textColor = Color.white;
        GUIStyle btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };
        float cx = Screen.width / 2f, cy = Screen.height / 2f;

        if (state == GameState.ClassSelect)
        {
            GUI.DrawTexture(new Rect(cx - 300, cy - 190, 600, 380), uiBgTex);
            GUI.Label(new Rect(cx - 280, cy - 170, 560, 45), "PIXEL GUARDIANS: WORLD RESCUE", titleStyle);
            GUI.Label(new Rect(cx - 250, cy - 115, 500, 30), "Chon Class nhan vat de bat dau giai cuu the gioi:", textStyle);

            if (GUI.Button(new Rect(cx - 260, cy - 65, 520, 60), "WARRIOR (Chien binh)\nMau cao | Can chien kiem | Luot nhanh | Skill (E): Vong xoai kiem", btnStyle))
                StartNewGame(HeroClass.Warrior);
            if (GUI.Button(new Rect(cx - 260, cy + 10, 520, 60), "MAGE (Phap su)\nTam xa AOE | Sat thuong phep cao | Skill (E): Mua bang lam cham", btnStyle))
                StartNewGame(HeroClass.Mage);
            if (GUI.Button(new Rect(cx - 260, cy + 85, 520, 60), "RANGER (Cung thu)\nToc do ban nhanh | Co dong cao | Skill (E): Ban dan chum 9 ten", btnStyle))
                StartNewGame(HeroClass.Ranger);
        }
        else if (state == GameState.Playing)
        {
            GUI.DrawTexture(new Rect(15, 15, 260, 115), uiBgTex);
            DrawBar(new Rect(25, 25, 240, 20), hp / maxHp, Color.red, $"HP: {(int)hp}/{(int)maxHp}");
            DrawBar(new Rect(25, 52, 240, 20), mp / maxMp, new Color(0.2f, 0.5f, 1f), $"MP: {(int)mp}/{(int)maxMp}");
            GUI.Label(new Rect(25, 78, 240, 22), $"LV: {level} | Gold: {gold} | Wave: {currentWave}/{maxWaves}", textStyle);
            GUI.Label(new Rect(25, 102, 240, 22), skillTimer <= 0 ? "Skill (E/R): SAN SANG" : $"Skill (E/R): {skillTimer:F1}s", textStyle);
            GUI.Label(new Rect(15, Screen.height - 30, 650, 25), "WASD: Di chuyen | Space: Luot (Dash) | Chuot Trai: Tan cong | E/R: Ky nang", textStyle);
        }
        else if (state == GameState.Shop)
        {
            GUI.DrawTexture(new Rect(cx - 280, cy - 210, 560, 420), uiBgTex);
            GUI.Label(new Rect(cx - 260, cy - 195, 520, 40), $"HOAN THANH WAVE {currentWave}! - CUA HANG", titleStyle);
            GUI.Label(new Rect(cx - 240, cy - 150, 480, 28), $"Vang: {gold} Gold | ATK: {atk} | HP: {maxHp} | SPD: {spd:F1}", textStyle);

            if (GUI.Button(new Rect(cx - 240, cy - 110, 480, 40), "Tang Sat Thuong (+8 ATK) - 30 Gold", btnStyle) && gold >= 30) { gold -= 30; atk += 8; }
            if (GUI.Button(new Rect(cx - 240, cy - 62, 480, 40), "Tang Mau Toi Da (+35 HP & Hoi day mau) - 30 Gold", btnStyle) && gold >= 30) { gold -= 30; maxHp += 35; hp = maxHp; }
            if (GUI.Button(new Rect(cx - 240, cy - 14, 480, 40), "Tang Toc Do Chay (+0.6 SPD) - 30 Gold", btnStyle) && gold >= 30) { gold -= 30; spd += 0.6f; }
            if (GUI.Button(new Rect(cx - 240, cy + 34, 480, 40), $"Nang Cap Vu Khi (Lv.{weaponLv} -> Lv.{weaponLv + 1}, +15 ATK) - 60 Gold", btnStyle) && gold >= 60) { gold -= 60; weaponLv++; atk += 15; }
            if (!hasThornArmor && GUI.Button(new Rect(cx - 240, cy + 82, 235, 40), "Giap Phan Sat Thuong - 80G", btnStyle) && gold >= 80) { gold -= 80; hasThornArmor = true; }
            if (!hasLifesteal && GUI.Button(new Rect(cx + 5, cy + 82, 235, 40), "Nhan Hut Mau (8%) - 80G", btnStyle) && gold >= 80) { gold -= 80; hasLifesteal = true; }

            if (GUI.Button(new Rect(cx - 120, cy + 145, 240, 45), $"VAO WAVE {currentWave + 1} >>", btnStyle))
            {
                hp = maxHp; mp = maxMp; StartWave(currentWave + 1); state = GameState.Playing;
            }
        }
        else if (state == GameState.GameOver || state == GameState.Victory)
        {
            GUI.DrawTexture(new Rect(cx - 240, cy - 110, 480, 220), uiBgTex);
            GUI.Label(new Rect(cx - 220, cy - 85, 440, 50), state == GameState.Victory ? "CHIEN THANG! THE GIOI DA DUOC GIAI CUU!" : "GAME OVER - BAN DA HY SINH!", titleStyle);
            GUI.Label(new Rect(cx - 200, cy - 25, 400, 30), $"Wave dat duoc: {currentWave}/{maxWaves} | Vang: {gold}", textStyle);
            if (GUI.Button(new Rect(cx - 110, cy + 30, 220, 45), "CHOI LAI", btnStyle)) state = GameState.ClassSelect;
        }
    }

    void DrawBar(Rect r, float pct, Color col, string label)
    {
        GUI.color = new Color(0.2f, 0.2f, 0.2f); GUI.DrawTexture(r, uiBarTex);
        GUI.color = col; GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(pct), r.height), uiBarTex);
        GUI.color = Color.white;
        GUI.Label(r, label, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold });
    }

    // ==================== INPUT ====================
    Vector2 GetMoveInput()
    {
        float x = 0, y = 0;
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb != null) { if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1; if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1; if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1; if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1; }
#else
        x = Input.GetAxisRaw("Horizontal"); y = Input.GetAxisRaw("Vertical");
#endif
        return new Vector2(x, y);
    }
    bool GetDashPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Space);
#endif
    }
    bool GetSkillPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && (Keyboard.current.eKey.wasPressedThisFrame || Keyboard.current.rKey.wasPressedThisFrame);
#else
        return Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.R);
#endif
    }
    bool GetAttackHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }
    Vector2 GetMouseWorldPos()
    {
        Vector3 m = Vector3.zero;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) m = Mouse.current.position.ReadValue();
#else
        m = Input.mousePosition;
#endif
        m.z = -cam.transform.position.z; return cam.ScreenToWorldPoint(m);
    }

    // ==================== HỖ TRỢ SPRITE & ĐỐI TƯỢNG ====================
    GameObject CreateObj(string name, Sprite sp, float scale, int sortOrder)
    {
        GameObject o = new GameObject(name);
        var sr = o.AddComponent<SpriteRenderer>();
        sr.sprite = sp;
        sr.sortingLayerName = topSortingLayer;
        sr.sortingOrder = sortOrder;
        o.transform.localScale = Vector3.one * scale;
        return o;
    }

    void ClearAllEntities()
    {
        if (playerObj) Destroy(playerObj);
        if (weaponObj) Destroy(weaponObj);
        foreach (var e in enemies) DestroyEnemyObj(e);
        foreach (var p in projectiles) if (p.obj) Destroy(p.obj);
        foreach (var d in drops) if (d.obj) Destroy(d.obj);
        foreach (var f in effects) if (f.obj) Destroy(f.obj);
        enemies.Clear(); projectiles.Clear(); drops.Clear(); effects.Clear();
    }

    void EnsureFallbackSprites()
    {
        if (!spWarrior) spWarrior = MakeCircleSprite(Color.blue, 16);
        if (!spMage) spMage = MakeCircleSprite(Color.magenta, 16);
        if (!spRanger) spRanger = MakeCircleSprite(Color.green, 16);
        if (!spWeaponSword) spWeaponSword = MakeCircleSprite(Color.gray, 8);
        if (!spWeaponStaff) spWeaponStaff = MakeCircleSprite(Color.cyan, 8);
        if (!spWeaponBow) spWeaponBow = MakeCircleSprite(new Color(0.6f, 0.3f, 0.1f), 8);
        if (!spEnemyFast) spEnemyFast = MakeCircleSprite(Color.red, 14);
        if (!spEnemyTank) spEnemyTank = MakeCircleSprite(new Color(0.4f, 0.1f, 0.1f), 16);
        if (!spEnemyRanged) spEnemyRanged = MakeCircleSprite(new Color(1f, 0.5f, 0f), 14);
        if (!spEnemyBomber) spEnemyBomber = MakeCircleSprite(Color.black, 14);
        if (!spBoss) spBoss = MakeCircleSprite(new Color(0.4f, 0f, 0.5f), 20);
        if (!spGold) spGold = MakeCircleSprite(Color.yellow, 10);
        if (!spHpPot) spHpPot = MakeCircleSprite(Color.red, 10);
        if (!spMpPot) spMpPot = MakeCircleSprite(Color.cyan, 10);

        spBulletPlayer = MakeCircleSprite(Color.yellow, 8);
        spBulletEnemy = MakeCircleSprite(new Color(1f, 0.2f, 0.2f), 8);
        spCircle = MakeCircleSprite(Color.white, 16);
        spSquare = Sprite.Create(MakeSolidTex(Color.white), new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1);
    }

    Sprite MakeCircleSprite(Color col, int size)
    {
        Texture2D tex = new Texture2D(size, size) { filterMode = FilterMode.Point };
        float r = size / 2f;
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                tex.SetPixel(x, y, Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) <= r ? col : new Color(0, 0, 0, 0));
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    Texture2D MakeSolidTex(Color c) { Texture2D t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }
}