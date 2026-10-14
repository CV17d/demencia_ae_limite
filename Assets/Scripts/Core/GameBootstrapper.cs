// ============================================================================
// GameBootstrapper.cs — Auto-setup & One-Click Game Runner for Unity
// ============================================================================
// You can drop this script onto ANY empty GameObject in an empty scene,
// or check "AutoBootOnPlay", and it will programmatically construct:
//   1. Directional Sun & Ambient Lighting
//   2. Core Managers GameObject (GameManager, LevelGenerator, LevelBuilder, etc.)
//   3. Player FPS Rig (CharacterController, Camera, WeaponHandler, Starting Weapon)
//   4. UI & Debug HUD overlay (Health, Sanity, Floors, Ammo, Fichas)
//   5. Automatically boots Floor 1 and starts the run!
// ============================================================================

using UnityEngine;
using DemenciaNoLimite.Boss;
using DemenciaNoLimite.Core;
using DemenciaNoLimite.Core.Data;
using DemenciaNoLimite.Horror;
using DemenciaNoLimite.Level;
using DemenciaNoLimite.Player;
using DemenciaNoLimite.UI;

namespace DemenciaNoLimite
{
    [DefaultExecutionOrder(-500)]
    public class GameBootstrapper : MonoBehaviour
    {
        [Header("Quick Start")]
        [Tooltip("If true, automatically creates everything on Awake and starts Floor 1.")]
        [SerializeField] private bool _autoBootOnPlay = true;

        [Header("Starting Weapon Config")]
        [SerializeField] private WeaponData _customStartingWeapon;

        private void Awake()
        {
            if (_autoBootOnPlay)
            {
                BootstrapGame();
            }
        }

        [ContextMenu("Boot Game Now")]
        public void BootstrapGame()
        {
            Debug.Log("<color=cyan><b>[DEMENCIA NO LIMITE]</b> Iniciando auto-construcción del juego...</color>");

            EnsureLighting();
            var managers = EnsureCoreManagers();
            var player = EnsurePlayerRig();
            EnsureUI();

            // Link managers to GameManager if needed
            var gm = GameManager.Instance;
            if (gm != null)
            {
                // Start run on next frame so all Awakes/Starts finish cleanly
                StartCoroutine(DelayedStartRun(gm));
            }
        }

        private System.Collections.IEnumerator DelayedStartRun(GameManager gm)
        {
            yield return null; // Wait 1 frame
            gm.StartNewRun();
            Debug.Log("<color=green><b>[DEMENCIA NO LIMITE]</b> ¡Run iniciada! Bienvenido a la Torre de la Frontera.</color>");
        }

        private void EnsureLighting()
        {
            // Create Directional Light if none exists
            if (FindAnyObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("Directional_Sun");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(0.85f, 0.9f, 1.0f);
                light.intensity = 0.8f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                RenderSettings.ambientLight = new Color(0.2f, 0.22f, 0.28f);
            }
        }

        private GameObject EnsureCoreManagers()
        {
            var existingGM = FindAnyObjectByType<GameManager>();
            if (existingGM != null) return existingGM.gameObject;

            var managerGo = new GameObject("=== CORE_MANAGERS ===");

            // 1. Core Systems
            var gm = managerGo.AddComponent<GameManager>();
            var levelGen = managerGo.AddComponent<LevelGenerator>();
            var levelBuilder = managerGo.AddComponent<LevelBuilder>();
            var aiDirector = managerGo.AddComponent<AIDirector>();
            var bossMath = managerGo.AddComponent<AdaptiveBossController>();
            var elevator = managerGo.AddComponent<ElevatorEventSystem>();
            var degradation = managerGo.AddComponent<MentalDegradationSystem>();

            // 2. Spawners
            var enemySpawner = managerGo.AddComponent<EnemySpawner>();
            var hazardSpawner = managerGo.AddComponent<HazardSpawner>();
            var bossEncounter = managerGo.AddComponent<BossEncounterManager>();

            // 3. Horror & Audio
            managerGo.AddComponent<MentalDegradationHook>();
            managerGo.AddComponent<HorrorAudioManager>();
            managerGo.AddComponent<ScreenShakeController>();

            // 4. Populate default sample pacts & merchant items into ElevatorEventSystem
            PopulateElevatorSampleData(elevator);

            return managerGo;
        }

        private GameObject EnsurePlayerRig()
        {
            var existingPlayer = GameObject.FindGameObjectWithTag("Player");
            if (existingPlayer != null) return existingPlayer;

            // Root Player Capsule
            var playerGo = new GameObject("Player_João");
            playerGo.tag = "Player";
            playerGo.transform.position = new Vector3(0f, 1.5f, 0f);

            var cc = playerGo.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            var health = playerGo.AddComponent<PlayerHealth>();
            var inventory = playerGo.AddComponent<PlayerInventory>();
            var controller = playerGo.AddComponent<PlayerController>();

            // Camera Rig
            var camGo = new GameObject("PlayerCamera");
            camGo.transform.parent = playerGo.transform;
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 75f;
            camGo.AddComponent<AudioListener>();

            var weaponHandler = playerGo.AddComponent<PlayerWeaponHandler>();

            // Create starting weapon if not assigned
            WeaponData startingWeapon = _customStartingWeapon;
            if (startingWeapon == null)
            {
                startingWeapon = ScriptableObject.CreateInstance<WeaponData>();
                startingWeapon.WeaponID = "PISTOL_IMPROVISED";
                startingWeapon.DisplayName = "Pistola Fronteriza Cal.9";
                startingWeapon.Type = WeaponType.Hitscan;
                startingWeapon.BaseDamage = 18f;
                startingWeapon.FireRate = 3.5f;
                startingWeapon.MagazineSize = 12;
                startingWeapon.ReloadTime = 1.2f;
                startingWeapon.MaxRange = 60f;
                startingWeapon.SpreadAngle = 1.2f;
                startingWeapon.RecoilStrength = 1.8f;
            }

            inventory.AddWeapon(startingWeapon);

            return playerGo;
        }

        private void EnsureUI()
        {
            var existingHUD = FindAnyObjectByType<HUDController>();
            if (existingHUD == null)
            {
                var uiGo = new GameObject("=== UI_CONTROLLERS ===");
                uiGo.AddComponent<HUDController>();
                uiGo.AddComponent<ElevatorUIController>();
                uiGo.AddComponent<BossHealthBarUI>();
                uiGo.AddComponent<RuntimeDebugOverlay>(); // Draws on-screen graphics via OnGUI
            }
        }

        private void PopulateElevatorSampleData(ElevatorEventSystem elevator)
        {
            // Add a sample Cursed Pact
            var pact = ScriptableObject.CreateInstance<CursedPactData>();
            pact.PactID = "PACT_ADRENALINE_DEMON";
            pact.DisplayName = "Pacto de la Sangre Hirviente";
            pact.FlavorText = "Tus piernas se sobrecargan con adrenalina cibernética, pero los mutantes del piso aprenden a arrojar escombros letales.";
            pact.BonusMoveSpeed = 1.8f;
            pact.BonusDamage = 6f;
            pact.EnemyDamageMultiplier = 1.3f;
            pact.GrantThrowDebris = true;
            pact.DegradationIncrease = 0.04f;

            // Add a sample Merchant Item
            var medkit = ScriptableObject.CreateInstance<MerchantItemData>();
            medkit.ItemID = "ITEM_STIM_HEAL";
            medkit.DisplayName = "Inyector de Sangre Sintética";
            medkit.Description = "Restaura 45 HP inmediatamente.";
            medkit.Cost = 25;
            medkit.HealAmount = 45f;

            // Reflect into Elevator lists
            var pactField = typeof(ElevatorEventSystem).GetField("_pactPool", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var itemField = typeof(ElevatorEventSystem).GetField("_merchantPool", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (pactField != null)
            {
                var list = pactField.GetValue(elevator) as System.Collections.Generic.List<CursedPactData>;
                list?.Add(pact);
            }
            if (itemField != null)
            {
                var list = itemField.GetValue(elevator) as System.Collections.Generic.List<MerchantItemData>;
                list?.Add(medkit);
            }
        }
    }
}
