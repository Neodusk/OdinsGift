using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using Mono.Security.Cryptography;
using MonoMod.Utils;
using PlayFab.ClientModels;
using UnityEngine;
using UnityEngine.UI;
using valheimmod;
using static valheimmod.valheimmod;

namespace valheimmod
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    //[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    internal partial class valheimmod : BaseUnityPlugin
    {
        public const string PluginGUID = "com.jotunn.valheimmod";
        public const string PluginName = "valheimmod";
        public const string PluginVersion = "0.0.1";
        public static valheimmod Instance;
        public static bool allowForsakenPower = true;
        private float dpadDownPressTime = -1f;
        private bool dpadDownHeld = false;
        private bool forsakenPowerTriggered = false;
        private const float holdThreshold = 0.35f; // seconds
        public static int currentDay = 0;
        public static bool LoggedIn = false;
        public static bool justDied = false;


        // Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        public class ModInput
        {
            public static ConfigEntry<KeyCode> SpecialRadialKeyConfig;
            public static ConfigEntry<InputManager.GamepadButton> SpecialRadialGamepadCloseConfig;
            public static ConfigEntry<InputManager.GamepadButton> SpecialRadialGamepadConfig;
            public static ButtonConfig SpecialRadialButton { get; private set; }

            public static void AddInputs(ConfigFile config)
            {
                SpecialRadialKeyConfig = config.Bind(
                   "Controls",
                   "SpecialRadialKey",
                   KeyCode.H,
                   new ConfigDescription("Key to activate special jump")
                );
                SpecialRadialGamepadConfig = config.Bind(
                   "Controls",
                   "SpecialRadialKey Gamepad",
                   InputManager.GamepadButton.DPadDown,
                   new ConfigDescription("Gamepad button to activate special jump")
                );

                SpecialRadialGamepadCloseConfig = config.Bind(
                   "Controls",
                   "SpecialRadialKeyClose",
                   InputManager.GamepadButton.ButtonEast,
                   new ConfigDescription("Gamepad button to close the radial menu")
                );


                // Register the key with Jotunn's InputManager
                SpecialRadialButton = new ButtonConfig
                {
                    Name = "SpecialRadialButton",
                    Key = SpecialRadialKeyConfig.Value,
                    Config = SpecialRadialKeyConfig,
                    GamepadConfig = SpecialRadialGamepadConfig,
                    HintToken = "$special_jump",
                    BlockOtherInputs = true,
                };

                InputManager.Instance.AddButton("com.jotunn.valheimmod", SpecialRadialButton);
            }
            public static bool IsSpecialRadialButtonHeld()
            {
                if (ZInput.GetButton(ModInput.SpecialRadialButton.Name) && !RadialMenuIsOpen)
                {
                    ShowRadialMenu();
                }
                return ZInput.GetButton(ModInput.SpecialRadialButton.Name);
            }
        }


        private void AddInputs()
        {
            // This method is called to add custom inputs to the game
            // You can add more inputs here if needed
            Config.SaveOnConfigSet = true;
            ModInput.AddInputs(Config);
        }

        private void LoadAssets()
        {
            // https://valheim-modding.github.io/Jotunn/tutorials/asset-loading.html
            string modPath = Path.GetDirectoryName(Info.Location);

            // Load texture from filesystem
            ModAbilities.SpecialJump.Instance.texture = AssetUtils.LoadTexture(Path.Combine(modPath, "Assets/specialjump.png"));
            ModAbilities.SpecialTeleport.Instance.texture = AssetUtils.LoadTexture(Path.Combine(modPath, "Assets/teleport.png"));
            ModAbilities.SpectralArrow.Instance.texture = AssetUtils.LoadTexture(Path.Combine(modPath, "Assets/spectral_arrow.png"));
            ModAbilities.ValhallaDome.Instance.texture = AssetUtils.LoadTexture(Path.Combine(modPath, "Assets/turtle_dome.png"));
            int maxOverlayTextures = 3; // Number of textures to load for the overlay
            for (int i = 1; i <= maxOverlayTextures; i++)
            {
                var numberTexture = AssetUtils.LoadTexture(Path.Combine(modPath, $"Assets/spectral_arrow{i}.png"));
                if (numberTexture != null)
                {
                    ModAbilities.SpectralArrow.Instance.textures[i - 1] = Sprite.Create(numberTexture, new Rect(0, 0, numberTexture.width, numberTexture.height), new Vector2(0.5f, 0.5f));
                }
                else
                {
                    Jotunn.Logger.LogError($"Failed to load number texture: {i}");
                }
            }
            if (ModAbilities.SpecialJump.Instance.texture == null)
            {
                Jotunn.Logger.LogError("Failed to load SpecialJumpTexture! Check if the PNG is valid and not corrupted.");
            }
            RadialSegmentSprites = new Sprite[4];
            RadialSegmentHighlightSprites = new Sprite[4];
            string[] segmentFiles = { "radial_n.png", "radial_e.png", "radial_s.png", "radial_w.png" };
            string[] segmentHighlightFiles = { "rh_n.png", "rh_e.png", "rh_s.png", "rh_w.png" };
            for (int i = 0; i < segmentFiles.Length; i++)
            {
                var tex = AssetUtils.LoadTexture(Path.Combine(modPath, $"Assets", segmentFiles[i]));
                RadialSegmentSprites[i] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                var texHighlight = AssetUtils.LoadTexture(Path.Combine(modPath, $"Assets", segmentHighlightFiles[i]));
                RadialSegmentHighlightSprites[i] = Sprite.Create(texHighlight, new Rect(0, 0, texHighlight.width, texHighlight.height), new Vector2(0.5f, 0.5f));
            }

            // Print Embedded Resources
            Jotunn.Logger.LogInfo($"Embedded resources: {string.Join(", ", typeof(valheimmod).Assembly.GetManifestResourceNames())}");

            // Load asset bundles from embedded resources
        }

        private void AddLocs()
        {

            // Use the instance of the CustomLocalization object instead of trying to call it statically  
            Localization.AddTranslation("English", new Dictionary<string, string>
            {
                {   "special_jumpeffect", "Super jump" },
                {   "pending_special_jumpeffect", "Super jump" },
                {   "pending_special_jumpeffect_start", "You feel lighter" },
                {   "pending_special_jumpeffect_stop", "You feel heavier" },
                {   "special_jumpeffect_tooltip", "A wind gust aids you." },
                {   "pending_teleport_effect", "Hearth"},
                {   "special_teleport_tooltip", "Teleporting Home"},
                {   "pending_teleporteffect_stop_cancelled", "Teleport Cancelled"},
                {   "pending_teleporteffect_stop_complete", "Traveling..."},
                {   "$teleport_effect", "Hearth"},
                {   "$special_teleport_cd_tooltip", "Cannot instant travel home right now."},
                {   "teleporteffect_start", "Traveling.."},
                {   "$teleporteffect_stop", "You can now hearth home."},
                {   "$teleporteffect_cd", "You can not hearth home until tomorrow."},
                {   "spectral_arrow_effect", "Spectral Arrow"},
                {   "spectral_arrow_start", "Choose your next 3 shots wisely.."},
                {   "spectral_arrow_cd_tooltip", "You are worn out from the last 3 spectral arrows."},
                {   "spectral_arrow_effect_tooltip", "Your next 3 shots will be spectral arrows."},
                {   "spectral_arrow_cd_start", "You can not fire anymore spectral arrows right now."},
                {   "spectral_arrow_cd_stop", "You can now fire spectral arrows."},
                {   "cd_dome_effect", "Valhalla Dome"},
                {   "cd_dome_tooltip", ""},
                {   "cd_domeeffect_start", ""},
                {   "dome_effect", "Valhalla Dome"},
                {   "dome_tooltip", "A shield of protection surrounds you."},
                {   "domeeffect_start", "You feel protected"},
                {   "domeeffect_stop", "You feel vulnerable"},
            });
        }

        private void Awake()
        {
            // Jotunn comes with its own Logger class to provide a consistent Log style for all mods using it
            Jotunn.Logger.LogInfo("valheimmod has landed");
            Harmony harmony = new Harmony(PluginGUID);
            Instance = this;
            harmony.PatchAll();
            LoadAssets();
            AddLocs();
            AddInputs();
            PrefabManager.OnPrefabsRegistered += ModAbilities.Effects.Register;
        }

        private void Update()
        {
            if (ZInput.instance != null)
            {
                // todo check that JoyGP and SpecialRadialButton are the same button before doing this to help with user remap
                string guardianPowerButton = "JoyGP";
                // Check if both are mapped to the same physical button

                if (ZInput.IsGamepadActive())
                {
                    allowForsakenPower = false; // default to false, only set to true if we detect the button press

                    if (ZInput.GetButtonDown(guardianPowerButton))
                    {
                        dpadDownPressTime = Time.time;
                        dpadDownHeld = true;
                        forsakenPowerTriggered = false;
                    }
                    if (dpadDownHeld)
                    {
                        // If held long enough and not yet triggered, activate Forsaken Power
                        if (!forsakenPowerTriggered && (Time.time - dpadDownPressTime) > holdThreshold)
                        {
                            if (Player.m_localPlayer != null)
                            {
                                allowForsakenPower = true;
                                Player.m_localPlayer.StartGuardianPower(); // Use the guardian power
                                forsakenPowerTriggered = true;
                            }
                        }

                        // If released before threshold, show radial menu
                        if (ZInput.GetButtonUp(ModInput.SpecialRadialButton.Name))
                        {
                            dpadDownHeld = false;
                            if (!forsakenPowerTriggered && (Time.time - dpadDownPressTime) <= holdThreshold)
                            {
                                if (!RadialMenuIsOpen)
                                {
                                    ShowRadialMenu();
                                }
                                else
                                {
                                    CloseRadialMenu();
                                }
                            }
                        }
                    }
                }


                else if (ZInput.GetButtonDown(ModInput.SpecialRadialButton.Name))
                {
                    if (!RadialMenuIsOpen)
                    {
                        ShowRadialMenu();
                    }
                    else
                    {
                        // other keys to close are handled in the HandleRadialMenu method
                        CloseRadialMenu();
                    }
                }
                if (RadialMenuIsOpen)
                {
                    HandleRadialMenu();
                }
                if (!RadialMenuIsOpen)
                {
                    ModAbilities.CallSpecialAbilities();
                }
                if (ModAbilities.SpecialTeleport.Instance?.SpecialEffect?.StatusEffect != null)
                {
                    if (EnvMan.instance != null)
                    {
                        int day = EnvMan.instance.GetDay();
                        if (currentDay != day)
                        {
                            if (Player.m_localPlayer != null && Player.m_localPlayer.IsPlayer())
                            {
                                if (Player.m_localPlayer.m_seman.HaveStatusEffect(ModAbilities.SpecialTeleport.Instance.SpecialEffect.StatusEffect.m_nameHash))
                                {
                                    Player.m_localPlayer.m_seman.RemoveStatusEffect(ModAbilities.SpecialTeleport.Instance.SpecialEffect.StatusEffect.m_nameHash, false);

                                }
                                currentDay = day;
                            }
                        }
                    }
                }
                if (ModAbilities.ValhallaDome.Instance?.SpecialCDEffect?.StatusEffect != null && ModAbilities.ValhallaDome.Instance.SpecialEffect?.StatusEffect != null)
                {
                    if (Player.m_localPlayer != null)
                    {
                        if (!Player.m_localPlayer.m_seman.HaveStatusEffect(ModAbilities.ValhallaDome.Instance.SpecialEffect.StatusEffect.m_nameHash) &&
                            ModAbilities.ValhallaDome.Instance.abilityUsed &&
                            Player.m_localPlayer != null && Player.m_localPlayer.IsPlayer())
                        {
                            ModAbilities.ValhallaDome.Instance.abilityUsed = false; // Reset the ability used flag
                            Player.m_localPlayer.m_seman.AddStatusEffect(ModAbilities.ValhallaDome.Instance.SpecialCDEffect.StatusEffect, true);
                        }
                    }
                }
                ModAbilities.Effects.Save();
                if (!LoggedIn)
                {
                    if (Player.m_localPlayer != null)
                    {
                        if (Player.m_localPlayer.m_seman != null)
                        {
                            Jotunn.Logger.LogInfo("Logged in. Loading Player Ability Cooldowns");
                            LoggedIn = true;
                            ModAbilities.Effects.Load();
                        }
                    }
                }
                if (justDied)
                {
                    if (Player.m_localPlayer != null)
                    {
                        if (Player.m_localPlayer.m_seman != null)
                        {
                            Jotunn.Logger.LogInfo("Player has died, loading special ability states.");
                            ModAbilities.Effects.Load(true);
                            justDied = false;
                        }
                    }
                }
            }
        }

        public static class JumpState
        {
            public static Dictionary<Character, bool> SpecialJumpActive = new Dictionary<Character, bool>();
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
        class Jump_Patch
        {
            static void Prefix(Character __instance)
            {
                if (__instance.IsPlayer())
                {
                    bool specialJump = false;
                    if (Player.m_localPlayer != null && Player.m_localPlayer == __instance)
                    {
                        specialJump = ModAbilities.SpecialJump.Instance.Triggered;
                    }
                    JumpState.SpecialJumpActive[__instance] = specialJump;
                    if (specialJump)
                    {
                        __instance.m_jumpForce = ModAbilities.SpecialJump.Instance.specialForce;
                    }
                    else
                    {
                        Jotunn.Logger.LogInfo("Jumped with default jump key");
                        __instance.m_jumpForce = ModAbilities.SpecialJump.Instance.defaultForce; // Default jump force
                    }
                    //valheimmod.ModAbilities.SpecialJump.Instance.Triggered = false; // this flag is reset in the patch for fall damage to prevent fall damage from coming back early
                }
            }
        }

        [HarmonyPatch(typeof(Character), "UpdateGroundContact")]
        class Character_Landing_Patch
        {
            // Track previous grounded state per character
            static Dictionary<Character, bool> wasOnGround = new Dictionary<Character, bool>();

            static void Postfix(Character __instance)
            {
                if (!__instance.IsPlayer() || !JumpState.SpecialJumpActive.TryGetValue(__instance, out bool specialJump) || !specialJump)
                {
                    return;
                }
                Jotunn.Logger.LogInfo($"Character {__instance.m_name} is checking ground contact.");
                bool isOnGround = __instance.IsOnGround();

                // Get previous state (default to true to avoid false positives on first frame)
                bool prevOnGround = wasOnGround.TryGetValue(__instance, out var prev) ? prev : true;
                var currentVel = __instance.m_body.velocity;
                // Detect landing: was not on ground, now is on ground
                if (!prevOnGround && isOnGround)
                {
                    if (__instance.IsPlayer())
                    {
                        Jotunn.Logger.LogInfo("Player has landed!");
                        Vector3 targetVel = new Vector3(currentVel.x, 0f, currentVel.z);
                        __instance.m_body.velocity = targetVel;
                        JumpState.SpecialJumpActive[__instance] = false;
                    }
                }
                if (!prevOnGround && !isOnGround && __instance.m_body.velocity.y < -5f)
                {

                    float gentleFallSpeed = -6f; // Set to a gentle fall speed
                    Vector3 targetVel = new Vector3(currentVel.x, gentleFallSpeed, currentVel.z);
                    __instance.m_body.velocity = Vector3.Lerp(currentVel, targetVel, 0.1f);
                    Jotunn.Logger.LogInfo($"Gentle falling velocity: {__instance.m_body.velocity}");
                }

                // Update state
                wasOnGround[__instance] = isOnGround;
                bool s2;
                s2 = JumpState.SpecialJumpActive.TryGetValue(__instance, out bool sj) ? sj : false;
                Jotunn.Logger.LogInfo($"postfix updateground checking fall damage. Special jump active: {s2}");

            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyFallDamage))]
        class NoFallDamage_SEMan_Patch
        {
            static void Prefix(SEMan __instance, float baseDamage, ref float damage)
            {
                Character character = __instance.m_character;
                bool s2;
                s2 = JumpState.SpecialJumpActive.TryGetValue(character, out bool sj) ? sj : false;
                Jotunn.Logger.LogInfo($"nofalldmg checking fall damage. Special jump active: {s2}");
                if (character != null && character.IsPlayer() &&
                    JumpState.SpecialJumpActive.TryGetValue(character, out bool specialJump) && specialJump)
                {
                    damage = 0f;
                    Jotunn.Logger.LogInfo("Fall damage prevented by patch!");
                    ModAbilities.SpecialJump.Instance.Triggered = false; // Reset the flag here instead of in jump to prevent pre-emptive fall damage 

                }
            }
        }


        [HarmonyPatch(typeof(StatusEffect), "UpdateStatusEffect")]
        class StatusEffect_Update_Patch
        {
            private static Dictionary<Character, float> lastVfxTime = new Dictionary<Character, float>();
            private static void SpecialJumpSEPatch(StatusEffect __instance, Character ___m_character)
            {
                if (__instance.name == "PendingSpecialJumpEffect" && ___m_character != null && ___m_character.IsPlayer())
                {
                    // Only spawn if enough time has passed (e.g., 1 second)
                    float now = Time.time;
                    if (!lastVfxTime.TryGetValue(___m_character, out float lastTime) || now - lastTime > 1f)
                    {
                        lastVfxTime[___m_character] = now;

                        Jotunn.Logger.LogInfo("Pending special jump effect is active, spawning VFX");
                        var leafPuffPrefab = ZNetScene.instance.GetPrefab("vfx_bush_leaf_puff");
                        if (leafPuffPrefab != null)
                        {
                            var vfx = UnityEngine.Object.Instantiate(leafPuffPrefab, ___m_character.transform.position, Quaternion.identity);
                            vfx.transform.SetParent(___m_character.transform);
                        }
                    }
                }
            }
            static void Postfix(StatusEffect __instance, Character ___m_character)
            {
                SpecialJumpSEPatch(__instance, ___m_character);
            }
        }
        [HarmonyPatch(typeof(Player), "Update")]
        class Player_CameraBlock_RadialMenu_Patch
        {
            static bool wasRadialMenuOpen = false;

            static void Prefix(Player __instance)
            {
                if (RadialMenuIsOpen)
                {
                    if (ZInput.instance != null && ZInput.instance.m_mouseDelta != null)
                    {
                        ZInput.instance.m_mouseDelta.Disable();
                    }
                    wasRadialMenuOpen = true;
                }
                else if (wasRadialMenuOpen)
                {
                    // Re-enable mouse look when menu closes
                    if (ZInput.instance != null && ZInput.instance.m_mouseDelta != null)
                    {
                        ZInput.instance.m_mouseDelta.Enable();
                    }
                    wasRadialMenuOpen = false;
                }
            }
        }

        [HarmonyPatch(typeof(Player), "PlayerAttackInput")]
        class Player_AttackInput_RadialBlock_Patch
        {
            static bool Prefix(Player __instance, float dt)
            {
                if (radialMenuInstance != null && radialMenuInstance.activeSelf)
                {
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Attack), "ProjectileAttackTriggered")]
        class SpectralArrow_ProjectileAttack_Unified_Patch
        {
            static void Postfix(Attack __instance)
            {
                var character = __instance.m_character as Player;
                if (character == null)
                    return;

                if (!(character.m_seman?.HaveStatusEffect(valheimmod.ModAbilities.SpectralArrow.Instance.SpecialEffect.StatusEffect.m_nameHash) ?? false))
                    return;

                var weapon = character.GetCurrentWeapon();
                if (weapon != null && weapon.m_shared != null && weapon.m_shared.m_skillType == Skills.SkillType.Bows)
                {
                    // Initialize shots fired if not exists
                    if (!ModAbilities.SpectralArrow.Instance.ShotsFired.ContainsKey(character))
                        ModAbilities.SpectralArrow.Instance.ShotsFired[character] = 0;
                        
                    // Track shots fired
                    ModAbilities.SpectralArrow.Instance.ShotsFired[character]++;
                    Jotunn.Logger.LogInfo($"Spectral Arrow: {character.m_name} has fired {ModAbilities.SpectralArrow.Instance.ShotsFired[character]} spectral shots.");
                    
                    // Add weapon to list for cleanup later
                    if (!ModAbilities.SpectralArrow.Instance.weaponList.Contains(weapon))
                    {
                        Jotunn.Logger.LogInfo($"Spectral Arrow: Adding {weapon.m_shared.m_name} to weapon list.");
                        ModAbilities.SpectralArrow.Instance.weaponList.Add(weapon);
                    }

                    // After 3 shots, cancel the ability
                    if (ModAbilities.SpectralArrow.Instance.ShotsFired[character] >= 3)
                    {
                        Jotunn.Logger.LogInfo($"Spectral Arrow: {character.m_name} has fired 3 spectral shots, cancelling ability.");
                        ModAbilities.SpectralArrow.Instance.Cancel(character);
                    }
                }
            }
        }



        //  TODO: we should still patch item drop 
        [HarmonyPatch(typeof(Player), "UpdateAttackBowDraw")]
        class SpectralArrow_Draw_Patch
        {
            private static Dictionary<Player, bool> hasLoggedThisDraw = new Dictionary<Player, bool>();
            
            static void Prefix(Player __instance, ItemDrop.ItemData weapon, float dt)
            {

                if (weapon != null && weapon.m_shared != null && weapon.m_shared.m_skillType == Skills.SkillType.Bows)
                {
                    // store the weapon defaults into the dictionary if it does not exist
                    if (!ModAbilities.SpectralArrow.Instance.weaponDefaults.ContainsKey(weapon.m_shared.m_name))
                    {
                        ModAbilities.SpectralArrow.Instance.weaponDefaults[weapon.m_shared.m_name] = (weapon.m_shared.m_attack.m_projectileVel, weapon.m_shared.m_attack.m_drawDurationMin);
                        Jotunn.Logger.LogInfo($"Spectral Arrow: Saved default velocity for {weapon.m_shared.m_name}: {weapon.m_shared.m_attack.m_projectileVel} and draw time: {weapon.m_shared.m_attack.m_drawDurationMin}");
                    }
                    // add the weapon into the weapon list if it does not exist
                    if (!ModAbilities.SpectralArrow.Instance.weaponList.Contains(weapon))
                    {
                        Jotunn.Logger.LogInfo($"Spectral Arrow: Adding {weapon.m_shared.m_name} to weapon list.");
                        ModAbilities.SpectralArrow.Instance.weaponList.Add(weapon);
                    }
                    if (ModAbilities.SpectralArrow.Instance.SpecialEffect?.StatusEffect != null && 
                        __instance.m_seman.HaveStatusEffect(ModAbilities.SpectralArrow.Instance.SpecialEffect.StatusEffect.m_nameHash))
                    {
                        // Only log once per draw session to avoid spam
                        if (!hasLoggedThisDraw.ContainsKey(__instance) || !hasLoggedThisDraw[__instance])
                        {
                            Jotunn.Logger.LogInfo($"Spectral Arrow: Player {__instance.m_name} is drawing with spectral arrow effect active");
                            hasLoggedThisDraw[__instance] = true;
                        }
                        
                        // Modify weapon projectile velocity and draw time
                        weapon.m_shared.m_attack.m_projectileVel = ModAbilities.SpectralArrow.Instance.specialVelocity;
                        weapon.m_shared.m_attack.m_drawDurationMin = ModAbilities.SpectralArrow.Instance.specialDrawDurationMin;
                    }
                    else
                    {
                        // Reset the logging flag when not using spectral arrow
                        if (hasLoggedThisDraw.ContainsKey(__instance))
                            hasLoggedThisDraw[__instance] = false;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Player), "OnDeath")]        
        class Player_On_Death_Patch
        {
            static void Prefix(Player __instance)
            {
                if (__instance.IsPlayer())
                {
                    // reset special ability states
                    if (__instance.m_seman != null)
                    {
                        ModAbilities.Effects.SaveToPreferences(true);
                        justDied = true;
                        Jotunn.Logger.LogInfo($"Player has died, saving special ability states. {justDied}");
                    }
                }
            }
            static void Postfix(Player __instance)
            {
                if (__instance.IsPlayer())
                {
                    Jotunn.Logger.LogInfo("Player has died, resetting special ability states.");
                    // reset special ability states
                    if (__instance.m_seman != null)
                    {
                        JumpState.SpecialJumpActive[__instance] = false;
                        ModAbilities.SpectralArrow.Instance.Cancel(__instance);
                        // ModAbilities.Effects.Load(true);
                        // Jotunn.Logger.LogInfo("Player has died, loading special ability states.");
                    }

                }
            }
        }
        // [HarmonyPatch(typeof(Player), "OnRespawn")]
        // class Player_On_Respawn_Patch {
        //     static void Postfix(Player __instance)
        //     {
        //         if (__instance.IsPlayer())
        //         {
        //             // reset special ability states
        //             if (__instance.m_seman != null)
        //             {
        //                 Jotunn.Logger.LogInfo("Player has respawned, resetting special ability states.");
        //                 if (justDied)
        //                 {
        //                     ModAbilities.Effects.Load(true);
        //                     Jotunn.Logger.LogInfo("Player has died, loading special ability states.");
        //                     justDied = false;
        //                 }
        //             }

        //         }
        //     }

        // }


        [HarmonyPatch(typeof(Hud), nameof(Hud.InRadial))]
        class Hud_InRadial_RadialMenu_Patch
        {
            static void Postfix(ref bool __result)
            {
                if (RadialMenuIsOpen)
                {
                    __result = true;
                }
            }
        }
        [HarmonyPatch(typeof(Hud), "UpdateStatusEffects")]
        class Hud_UpdateStatusEffects_Patch
        {
            static void Postfix(Hud __instance, List<StatusEffect> statusEffects)
            {
                ModAbilities.Effects.UpdateStatusEffect(__instance, statusEffects);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.StartGuardianPower))]
        class Player_UseGuardianPower_Patch
        {
            static bool Prefix(Player __instance)
            {
                if (ZInput.IsGamepadActive())
                {
                    if (!allowForsakenPower)
                    {
                        Jotunn.Logger.LogInfo("Forsaken power use blocked by radial menu");
                        // Prevent the guardian power from being used
                        return false;
                    }
                    else
                    {
                        Jotunn.Logger.LogInfo("Forsaken power use allowed by radial menu");
                        // allowForsakenPower = false; // Reset the flag after use
                        return true;
                    }
                }
                return true;
            }

        }
        [HarmonyPatch(typeof(Player), "Awake")]
        class Player_Awake_DayTracker_Patch
        {
            static void Postfix(Player __instance)
            {
                if (__instance.IsPlayer())
                {
                    int day = EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;
                    currentDay = day;
                    Jotunn.Logger.LogInfo($"Player loaded in on day {currentDay}");

                }
            }
        }
        [HarmonyPatch(typeof(Player), "Awake")]
        public static class Player_Awake_Cleanup_Patch
        {
            static void Postfix(Player __instance)
            {
                // Only run for the local player
                if (!__instance.IsPlayer() || __instance == null)
                    return;

                ModAbilities.ValhallaDome.Instance.LastDomeUID = PlayerPrefs.GetString("Dome_LastDomeUID", "");
                if (string.IsNullOrEmpty(valheimmod.ModAbilities.ValhallaDome.Instance.LastDomeUID) ||
                string.IsNullOrEmpty(ModAbilities.ValhallaDome.Instance.dome_uid))
                {
                    Jotunn.Logger.LogInfo("Dome: LastDomeUID is empty, skipping dome cleanup.");
                    return;
                }
                if (ZNetScene.instance == null)
                {
                    Jotunn.Logger.LogWarning("Dome: ZNetScene is null, cannot clean up dome.");
                    return;
                }
                foreach (ZNetView znetView in ZNetScene.instance.m_instances.Values)
                {
                    if (znetView != null && znetView.IsValid())
                    {
                        var zdo = znetView.GetZDO();
                        if (zdo != null && zdo.GetString(ModAbilities.ValhallaDome.Instance.dome_uid, "") == valheimmod.ModAbilities.ValhallaDome.Instance.LastDomeUID)
                        {
                            znetView.ClaimOwnership();
                            znetView.Destroy();
                            Jotunn.Logger.LogInfo("Dome: Destroyed dome on login.");
                            break;
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Menu), nameof(Menu.OnLogout))]
        public static class Logout_Patch
        {
            static void Prefix()
            {
                Jotunn.Logger.LogInfo("Logout Prefix: Attempting Dome cleanup before menu logout.");
                valheimmod.ModAbilities.ValhallaDome.Instance.OnPlayerLogout();
                ModAbilities.Effects.SaveToPreferences(); // Save effects before logout
            }
        }
        [HarmonyPatch(typeof(Menu), nameof(Menu.OnQuit))]
        public static class Quit_Patch
        {
            static void Prefix()
            {
                Jotunn.Logger.LogInfo("Quit Prefix: Attempting Dome cleanup before menu logout.");
                valheimmod.ModAbilities.ValhallaDome.Instance.OnPlayerLogout();
                ModAbilities.Effects.SaveToPreferences(); // Save effects before logout
            }
        }
        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
        public static class ZNet_Disconnect_Cleanup_Patch
        {
            static void Prefix()
            {
                Jotunn.Logger.LogInfo("ZNet.Shutdown Prefix: Attempting Dome cleanup before disconnect.");
                valheimmod.ModAbilities.ValhallaDome.Instance.OnPlayerLogout();
                ModAbilities.Effects.SaveToPreferences(); // Save effects before logout
            }
        }
        [HarmonyPatch(typeof(FejdStartup), "TransitionToMainScene")]
        public static class FejdStartup_TransitionToMainScene_Patch
        {
            static void Prefix()
            {
                LoggedIn = false;
                Jotunn.Logger.LogInfo("Transitioning to main scene, resetting LoggedIn to false.");
            }
        }
    }
}