using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Uzi Stock Auto Fire", "NINJA WORKS", "1.0.0")]
    [Description("Lets the Uzi keep firing while the trigger is held with its stock extended.")]
    public class UziStockAutoFire : RustPlugin
    {
        private const float FireIntervalOverride = 0f;
        private const float DamageMultiplier = 1f;
        private const bool EstimateHitArea = true;
        private const bool ViewKick = true;
        private const float ViewKickScale = 1f;

        private const float MinFireInterval = 0.03f;
        private const float SimulationStep = 1f / 32f;
        private const float MaxTravelTime = 4f;
        private const int ProjectileMask = 1220225793;

        private static UziStockAutoFire _plugin;
        private static readonly Effect ReusableEffect = new Effect();

        private readonly Dictionary<ulong, FireState> _states = new Dictionary<ulong, FireState>();
        private readonly Dictionary<int, Dictionary<HitArea, uint>> _boneIdsBySkeleton = new Dictionary<int, Dictionary<HitArea, uint>>();
        private ProjectileSimulator _simulator;

        private class FireState
        {
            public BasePlayer Player;
            public Uzi Weapon;
            public Timer Timer;
            public int LastAmmo;
            public float LastClientShotTime = float.NegativeInfinity;
            public float AimconePenalty;
            public float LastShotTime = float.NegativeInfinity;
            public int ShotsFired;
        }

        private void Init()
        {
            _plugin = this;
        }

        private void OnServerInitialized()
        {
            _simulator = new GameObject("UziStockAutoFireSimulator").AddComponent<ProjectileSimulator>();
        }

        private void Unload()
        {
            foreach (FireState state in _states.Values)
                StopAutoFire(state);
            _states.Clear();

            if (_simulator != null)
                UnityEngine.Object.Destroy(_simulator.gameObject);

            _plugin = null;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
                return;

            ulong userId = player.userID.Get();
            FireState state;
            if (_states.TryGetValue(userId, out state))
            {
                StopAutoFire(state);
                _states.Remove(userId);
            }
        }

        private void OnWeaponFired(BaseProjectile weapon, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot shoot)
        {
            Uzi uzi = weapon as Uzi;
            if (uzi == null || player == null || !player.IsConnected)
                return;

            FireState state = GetState(player);
            float now = Time.time;
            state.LastClientShotTime = now;
            RecoverSpread(state, uzi, now);
            RegisterShot(state, uzi, now);

            if (!uzi.IsStockDeployed() || state.Timer != null)
                return;

            state.Weapon = uzi;
            state.LastAmmo = uzi.primaryMagazine.contents;
            state.Timer = timer.Every(GetFireInterval(uzi), () => AutoFireTick(state));
        }

        private FireState GetState(BasePlayer player)
        {
            ulong userId = player.userID.Get();
            FireState state;
            if (!_states.TryGetValue(userId, out state))
            {
                state = new FireState();
                _states[userId] = state;
            }
            state.Player = player;
            return state;
        }

        private void AutoFireTick(FireState state)
        {
            BasePlayer player = state.Player;
            Uzi uzi = state.Weapon;
            if (player == null || !player.IsConnected || uzi == null || player.GetHeldEntity() != uzi
                || !player.serverInput.IsDown(BUTTON.FIRE_PRIMARY) || !uzi.IsStockDeployed())
            {
                StopAutoFire(state);
                return;
            }

            int ammo = uzi.primaryMagazine.contents;
            if (ammo <= 0 || ammo > state.LastAmmo || !CanShootNow(state, player, uzi))
            {
                StopAutoFire(state);
                return;
            }

            float now = Time.time;
            bool aiming = player.modelState.aiming;
            RecoverSpread(state, uzi, now);
            if (!FireShot(player, uzi, GetWeaponAimCone(state, uzi, aiming)))
            {
                StopAutoFire(state);
                return;
            }

            RegisterShot(state, uzi, now);
            if (ViewKick && ViewKickScale > 0f)
                SendViewKick(uzi, aiming, ViewKickScale);

            state.LastAmmo = uzi.primaryMagazine.contents;
        }

        private static void StopAutoFire(FireState state)
        {
            if (state.Timer != null)
            {
                state.Timer.Destroy();
                state.Timer = null;
            }
            state.Weapon = null;
        }

        private static bool CanShootNow(FireState state, BasePlayer player, Uzi uzi)
        {
            if (player.IsDead() || player.IsSleeping() || player.IsWounded() || player.IsIncapacitated())
                return false;
            if (player.InGesture || player.IsHeadUnderwater())
                return false;
            if (uzi.ServerIsReloading() || !uzi.CanAttack())
                return false;

            Item item = uzi.GetItem();
            if (item == null || item.isBroken)
                return false;

            float clientShotCooldownEnd = state.LastClientShotTime + uzi.ScaleRepeatDelay(uzi.repeatDelay) + uzi.animationDelay;
            return !uzi.HasAttackCooldown() || uzi.NextAttackTime <= clientShotCooldownEnd + 0.001f;
        }

        private static float GetFireInterval(Uzi uzi)
        {
            float baseInterval = FireIntervalOverride > 0f ? FireIntervalOverride : uzi.repeatDelay;
            float scale = ProjectileWeaponMod.Mult(uzi, ProjectileWeaponMod.SelectRepeatDelay, ProjectileWeaponMod.SelectScalar);
            float offset = ProjectileWeaponMod.Sum(uzi, ProjectileWeaponMod.SelectRepeatDelay, ProjectileWeaponMod.SelectOffset);
            return Mathf.Max(MinFireInterval, baseInterval * scale + offset);
        }

        private static void RecoverSpread(FireState state, Uzi uzi, float now)
        {
            float idle = now - state.LastShotTime;
            if (idle > uzi.resetDuration)
                state.ShotsFired = 0;

            float recoverTime = idle - uzi.aimconePenaltyRecoverDelay;
            if (recoverTime > 0f && state.AimconePenalty > 0f)
            {
                float rate = uzi.aimConePenaltyMax / Mathf.Max(0.01f, uzi.aimconePenaltyRecoverTime);
                state.AimconePenalty = Mathf.Max(0f, state.AimconePenalty - rate * recoverTime);
            }
        }

        private static void RegisterShot(FireState state, Uzi uzi, float now)
        {
            state.ShotsFired++;
            state.AimconePenalty = Mathf.Min(state.AimconePenalty + uzi.aimconePenaltyPerShot, uzi.aimConePenaltyMax);
            state.LastShotTime = now;
        }

        private static float GetWeaponAimCone(FireState state, Uzi uzi, bool aiming)
        {
            float sightScale = ProjectileWeaponMod.Mult(uzi, ProjectileWeaponMod.SelectSightAimCone, ProjectileWeaponMod.SelectScalar);
            float sightOffset = ProjectileWeaponMod.Sum(uzi, ProjectileWeaponMod.SelectSightAimCone, ProjectileWeaponMod.SelectOffset);
            float hipScale = ProjectileWeaponMod.Mult(uzi, ProjectileWeaponMod.SelectHipAimCone, ProjectileWeaponMod.SelectScalar);
            float hipOffset = ProjectileWeaponMod.Sum(uzi, ProjectileWeaponMod.SelectHipAimCone, ProjectileWeaponMod.SelectOffset);

            float cone = uzi.aimCone;
            float penalty = state.AimconePenalty;

            RecoilProperties recoil = uzi.recoilProperties;
            int capacity = uzi.primaryMagazine.capacity;
            bool useCurve = recoil != null && recoil.overrideAimconeWithCurve && capacity > 0;
            if (useCurve)
            {
                cone += recoil.aimconeCurve.Evaluate((float)state.ShotsFired / capacity % 1f) * recoil.aimconeCurveScale;
                penalty = 0f;
            }

            cone = (cone + penalty) * sightScale + sightOffset;
            if (!aiming)
                cone += uzi.hipAimCone * hipScale + hipOffset;
            if (useCurve)
                cone *= recoil.aimconeProbabilityCurve.Evaluate(UnityEngine.Random.value);

            return Mathf.Max(0f, cone);
        }

        private static void SendViewKick(Uzi uzi, bool aiming, float scale)
        {
            float pitch = 0.8f;
            float yaw = UnityEngine.Random.Range(-0.3f, 0.3f);
            float duration = 0.06f;

            RecoilProperties recoil = uzi.recoilProperties;
            if (recoil != null)
            {
                float recoilPitch = Mathf.Abs(UnityEngine.Random.Range(recoil.recoilPitchMin, recoil.recoilPitchMax));
                float recoilYaw = UnityEngine.Random.Range(recoil.recoilYawMin, recoil.recoilYawMax);
                if (recoilPitch > 0.01f || Mathf.Abs(recoilYaw) > 0.01f)
                {
                    pitch = recoilPitch;
                    yaw = recoilYaw;
                    duration = UnityEngine.Random.Range(recoil.timeToTakeMin, recoil.timeToTakeMax);
                }
                if (aiming)
                    scale *= recoil.ADSScale;
            }

            scale *= ProjectileWeaponMod.Mult(uzi, ProjectileWeaponMod.SelectRecoil, ProjectileWeaponMod.SelectScalar);

            uzi.SendPunch(new Vector3(-pitch * scale, yaw * scale, 0f), Mathf.Max(0.02f, duration));
        }

        private bool FireShot(BasePlayer player, Uzi uzi, float weaponAimCone)
        {
            if (_simulator == null)
                return false;

            ItemDefinition ammoDefinition = uzi.primaryMagazine.ammoType;
            ItemModProjectile ammo = ammoDefinition != null ? ammoDefinition.GetComponent<ItemModProjectile>() : null;
            if (ammo == null)
                return false;

            GameObjectRef projectileRef = ammo.GetOverrideProjectile(uzi);
            GameObject projectileObject = projectileRef != null ? projectileRef.Get() : null;
            Projectile projectile = projectileObject != null ? projectileObject.GetComponent<Projectile>() : null;
            if (projectile == null)
                return false;

            RecoilProperties recoil = uzi.recoilProperties;
            float ammoSpreadScale = recoil == null ? 1f : (ammo.numProjectiles > 1 ? recoil.ammoAimconeScaleMultiProjectile : recoil.ammoAimconeScaleSingleProjectile);
            float aimCone = ammo.projectileSpread * ammoSpreadScale + weaponAimCone;
            float velocityScale = uzi.GetProjectileVelocityScale() * ProjectileWeaponMod.Mult(uzi, ProjectileWeaponMod.SelectVelocity, ProjectileWeaponMod.SelectScalar);

            Vector3 origin = player.eyes.position;
            Vector3 forward = player.eyes.BodyForward();
            Vector3 gravity = UnityEngine.Physics.gravity * projectile.gravityModifier;
            float thickness = uzi.GetOverrideProjectileThickness(projectile);
            Projectile.Modifier modifier = uzi.GetProjectileModifier();
            AttackEntity weaponPrefab = GetWeaponPrefab(uzi);

            uzi.ModifyAmmoCount(-1);
            uzi.SignalBroadcast(BaseEntity.Signal.Attack, string.Empty, null, GetAttackEffect(uzi), uzi.maxAttackEffectDistance);

            for (int i = 0; i < ammo.numProjectiles; i++)
            {
                Vector3 direction = AimConeUtil.GetModifiedAimConeDirection(aimCone, forward);
                Vector3 velocity = direction * (ammo.GetRandomVelocity() * velocityScale) + uzi.GetInheritedVelocity(player, direction);

                SendProjectileEffect(uzi, projectileRef.resourcePath, origin, velocity);

                _simulator.Add(new SimulatedBullet
                {
                    Shooter = player,
                    Weapon = uzi,
                    WeaponPrefab = weaponPrefab,
                    Projectile = projectile,
                    Ammo = ammo,
                    Modifier = modifier,
                    StartPosition = origin,
                    Position = origin,
                    Velocity = velocity,
                    Gravity = gravity,
                    Drag = projectile.drag,
                    Thickness = thickness,
                    FiredFrame = Time.frameCount
                });
            }

            uzi.UpdateItemCondition();
            uzi.SendNetworkUpdate();
            player.MarkHostileFor();
            player.MakeNoise(player.transform.position, BaseCombatEntity.ActionVolume.Loud);

            global::Rust.Ai.Gen2.NpcNoiseManager noise = SingletonComponent<global::Rust.Ai.Gen2.NpcNoiseManager>.Instance;
            if (noise != null)
                noise.OnWeaponShot(player, uzi);

            return true;
        }

        private bool ProcessHit(SimulatedBullet bullet, Ray ray, RaycastHit hit)
        {
            BaseEntity entity = hit.GetEntity();
            if (entity != null && (entity == bullet.Weapon || entity == bullet.Shooter))
                return false;

            ColliderInfo colliderInfo = hit.collider != null ? hit.collider.GetComponent<ColliderInfo>() : null;
            if (colliderInfo != null && !colliderInfo.HasFlag(ColliderInfo.Flags.Shootable))
                return false;

            BasePlayer victim = entity as BasePlayer;
            Shield activeShield;
            if (victim != null && victim.TryGetActiveShield(out activeShield) && activeShield.RaycastAgainstColliders(ray, BaseProjectile.maxDistance)
                && (activeShield.transform.position - ray.origin).sqrMagnitude < (victim.CenterPoint() - ray.origin).sqrMagnitude)
            {
                return false;
            }

            bool stops = entity == null || entity.ShouldBlockProjectiles();

            HitInfo info = new HitInfo();
            info.Initiator = bullet.Shooter;
            info.Weapon = bullet.Weapon;
            info.WeaponPrefab = bullet.WeaponPrefab;
            info.ProjectilePrefab = bullet.Projectile;
            info.ProjectileDistance = bullet.Distance + hit.distance;
            info.ProjectileVelocity = bullet.Velocity;
            info.ProjectileTravelTime = bullet.TravelTime + hit.distance / Mathf.Max(0.01f, bullet.Velocity.magnitude);
            info.ProjectileIntegrity = 1f;
            info.IsPredicting = false;
            info.DoHitEffects = bullet.Projectile.doHitEffects;
            info.DidHit = true;
            info.PointStart = bullet.StartPosition;
            info.PointEnd = hit.point;
            info.HitPositionWorld = hit.point;
            info.HitNormalWorld = hit.normal;
            info.HitEntity = entity;
            info.UseProtection = true;

            BaseCombatEntity areaTarget = victim != null ? victim : (BaseCombatEntity)(entity as global::Rust.Ai.Gen2.ScientistNPC2);
            if (EstimateHitArea && areaTarget != null)
            {
                uint boneId = GetBoneId(areaTarget, EstimateArea(areaTarget, hit.point));
                if (boneId != 0u)
                    info.HitBone = boneId;
            }

            bullet.Projectile.CalculateDamage(info, bullet.Modifier, 1f);
            info.damageTypes.ScaleAll(DamageMultiplier);

            if (stops)
                bullet.Ammo.ServerProjectileHit(info);

            if (entity != null)
            {
                entity.OnAttacked(info);
                bullet.Ammo.ServerProjectileHitEntity(info);

                Shield hitShield = entity as Shield;
                if (entity is BasePlayer || entity is BaseNpc || hitShield != null)
                {
                    info.HitPositionLocal = entity.transform.InverseTransformPoint(info.HitPositionWorld);
                    info.HitNormalLocal = entity.transform.InverseTransformDirection(info.HitNormalWorld);
                    info.HitMaterial = StringPool.Get(hitShield != null ? hitShield.GetHitMaterialString() : "Flesh");
                    Effect.server.ImpactEffect(info);
                }
            }

            global::Rust.Ai.Gen2.NpcNoiseManager noise = SingletonComponent<global::Rust.Ai.Gen2.NpcNoiseManager>.Instance;
            if (noise != null && bullet.Shooter != null)
                noise.OnProjectileHit(bullet.Shooter, info);

            return stops;
        }

        private static AttackEntity GetWeaponPrefab(BaseEntity weapon)
        {
            GameObject prefab = GameManager.server.FindPrefab(StringPool.Get(weapon.prefabID));
            return prefab != null ? prefab.GetComponent<AttackEntity>() : null;
        }

        private static string GetAttackEffect(BaseProjectile weapon)
        {
            string path = weapon.attackFX != null ? weapon.attackFX.resourcePath : string.Empty;

            ItemDefinition ammoType = weapon.primaryMagazine.ammoType;
            ItemModProjectile ammo = ammoType != null ? ammoType.GetComponent<ItemModProjectile>() : null;
            if (ammo != null && ammo.attackEffectOverride != null && ammo.attackEffectOverride.isValid)
                path = ammo.attackEffectOverride.resourcePath;

            if (weapon.children == null)
                return path;

            foreach (BaseEntity child in weapon.children)
            {
                ProjectileWeaponMod mod = child as ProjectileWeaponMod;
                if (mod == null)
                    continue;

                if (mod.isSilencer)
                {
                    path = mod.defaultSilencerEffect.resourcePath;
                    if (weapon.silencedAttack != null && weapon.silencedAttack.isValid)
                    {
                        path = weapon.silencedAttack.resourcePath;
                        GameObject silencedPrefab = weapon.silencedAttack.Get();
                        EffectSilencerSelect select;
                        GameObjectRef result;
                        if (silencedPrefab != null && silencedPrefab.TryGetComponent(out select) && select.GetEffectForSilencerType(mod.silencerType, out result))
                            path = result.resourcePath;
                    }
                    break;
                }

                if (mod.isMuzzleBrake)
                {
                    if (weapon.muzzleBrakeAttack != null && weapon.muzzleBrakeAttack.isValid)
                        path = weapon.muzzleBrakeAttack.resourcePath;
                    break;
                }
            }
            return path;
        }

        private static void SendProjectileEffect(BaseEntity weapon, string prefab, Vector3 position, Vector3 velocity)
        {
            Effect effect = ReusableEffect;
            effect.InitWithSourceEntity(Effect.Type.Projectile, weapon, position, velocity, null);
            effect.scale = 2f;
            effect.pooledString = prefab;
            effect.number = UnityEngine.Random.Range(1, 100);
            effect.targets = null;
            effect.distanceOverride = 0f;
            EffectNetwork.Send(effect);
        }

        private static HitArea EstimateArea(BaseCombatEntity victim, Vector3 hitPoint)
        {
            BasePlayer player = victim as BasePlayer;
            if (player != null)
            {
                float feet = player.transform.position.y;
                float eyeHeight = player.eyes.position.y - feet;
                if (eyeHeight < 0.5f)
                    return HitArea.Chest;

                float t = (hitPoint.y - feet) / eyeHeight;
                if (t >= 0.92f) return HitArea.Head;
                if (t >= 0.70f) return HitArea.Chest;
                if (t >= 0.58f) return HitArea.Stomach;
                return HitArea.Leg;
            }

            Bounds bounds = victim.bounds;
            float bottom = victim.transform.position.y + bounds.min.y;
            float height = bounds.size.y;
            if (height < 1.2f || height > 2.5f)
            {
                bottom = victim.transform.position.y;
                height = 1.8f;
            }

            float s = (hitPoint.y - bottom) / height;
            if (s >= 0.83f) return HitArea.Head;
            if (s >= 0.58f) return HitArea.Chest;
            if (s >= 0.50f) return HitArea.Stomach;
            return HitArea.Leg;
        }

        private uint GetBoneId(BaseCombatEntity victim, HitArea area)
        {
            SkeletonProperties skeleton = victim.skeletonProperties;
            if (skeleton == null || skeleton.bones == null)
                return 0u;

            Dictionary<HitArea, uint> boneIds;
            int key = skeleton.GetInstanceID();
            if (!_boneIdsBySkeleton.TryGetValue(key, out boneIds))
            {
                boneIds = new Dictionary<HitArea, uint>();
                foreach (SkeletonProperties.BoneProperty bone in skeleton.bones)
                {
                    if (bone == null || bone.bone == null || boneIds.ContainsKey(bone.area))
                        continue;
                    boneIds[bone.area] = StringPool.Get(bone.bone.name);
                }
                _boneIdsBySkeleton[key] = boneIds;
            }

            uint boneId;
            return boneIds.TryGetValue(area, out boneId) ? boneId : 0u;
        }

        private class SimulatedBullet
        {
            public BasePlayer Shooter;
            public BaseProjectile Weapon;
            public AttackEntity WeaponPrefab;
            public Projectile Projectile;
            public ItemModProjectile Ammo;
            public Projectile.Modifier Modifier;
            public Vector3 StartPosition;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 Gravity;
            public float Drag;
            public float Thickness;
            public float Distance;
            public float TravelTime;
            public float PendingTime;
            public int FiredFrame;
        }

        private class ProjectileSimulator : MonoBehaviour
        {
            private readonly List<SimulatedBullet> _bullets = new List<SimulatedBullet>();
            private readonly List<RaycastHit> _hits = new List<RaycastHit>();

            private void Awake()
            {
                enabled = false;
            }

            public void Add(SimulatedBullet bullet)
            {
                _bullets.Add(bullet);
                enabled = true;
            }

            private void Update()
            {
                if (_plugin == null)
                {
                    _bullets.Clear();
                    enabled = false;
                    return;
                }

                float deltaTime = Time.deltaTime;
                int frame = Time.frameCount;
                for (int i = _bullets.Count - 1; i >= 0; i--)
                {
                    SimulatedBullet bullet = _bullets[i];
                    if (bullet.FiredFrame == frame)
                        continue;

                    bullet.PendingTime += deltaTime;
                    bool alive = true;
                    while (alive && bullet.PendingTime >= SimulationStep)
                    {
                        bullet.PendingTime -= SimulationStep;
                        alive = Step(bullet);
                    }

                    if (!alive)
                        _bullets.RemoveAt(i);
                }

                if (_bullets.Count == 0)
                    enabled = false;
            }

            private bool Step(SimulatedBullet bullet)
            {
                if (bullet.TravelTime >= MaxTravelTime)
                    return false;

                Vector3 move = bullet.Velocity * SimulationStep;
                float length = move.magnitude;
                if (length > 0.0001f)
                {
                    Ray ray = new Ray(bullet.Position, move / length);
                    _hits.Clear();
                    GamePhysics.TraceAll(ray, bullet.Thickness, _hits, length, ProjectileMask, QueryTriggerInteraction.Ignore, bullet.Shooter);
                    if (_hits.Count > 1)
                        _hits.Sort(CompareHitDistance);

                    for (int i = 0; i < _hits.Count; i++)
                    {
                        if (_plugin.ProcessHit(bullet, ray, _hits[i]))
                            return false;
                    }
                }

                bullet.Position += move;
                bullet.Distance += length;
                bullet.TravelTime += SimulationStep;
                bullet.Velocity += bullet.Gravity * SimulationStep;
                bullet.Velocity -= bullet.Velocity * (bullet.Drag * SimulationStep);
                return true;
            }

            private static int CompareHitDistance(RaycastHit a, RaycastHit b)
            {
                return a.distance.CompareTo(b.distance);
            }
        }
    }
}
