using BepInEx;
using Jotunn.Entities;
using Jotunn.Managers;
using Mono.Security.Interface;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// This file contains special abilities for the Valheim mod

namespace valheimmod
{
    internal partial class valheimmod : BaseUnityPlugin
    {
    public Coroutine teleportCountdownCoroutine;
    /// <summary>
    /// Used to start the Teleport countdown coroutine. Must be in the main valheimmod class
    /// <param name="seconds"></param>
    public void StartTeleportCountdown(int seconds)
    {
        if (teleportCountdownCoroutine != null)
        {
            StopCoroutine(teleportCountdownCoroutine);
        }
        teleportCountdownCoroutine = StartCoroutine(ModAbilities.SpecialTeleport.Instance.TeleportCountdownCoroutine(seconds));
    }

        /// <summary>
        /// Contains methods for calling special abilities
        /// </summary>
        public class ModAbilities
        {
            public static List<SpecialAbilityBase> specialAbilities = GetAllAbilityInstances();
            
            /// <summary>
            /// Gets all instances of SpecialAbilityBase subclasses
            /// /// This method uses reflection to find all classes that inherit from SpecialAbilityBase
            /// and attempts to retrieve a static instance of each class.
            /// </summary>
            /// <returns></returns>
            public static List<SpecialAbilityBase> GetAllAbilityInstances()
            {
                var abilityTypes = typeof(SpecialAbilityBase).Assembly
                    .GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(SpecialAbilityBase)));

                List<SpecialAbilityBase> specialAbilities = new List<SpecialAbilityBase>();

                foreach (var type in abilityTypes)
                {
                    // Try to get a static field called "Instance"
                    var instanceField = type.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (instanceField != null)
                    {
                        var instance = instanceField.GetValue(null) as SpecialAbilityBase;
                        if (instance != null)
                            specialAbilities.Add(instance);
                        continue;
                    }

                    // Or try to get a static property called "Instance"
                    var instanceProp = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (instanceProp != null)
                    {
                        var instance = instanceProp.GetValue(null) as SpecialAbilityBase;
                        if (instance != null)
                            specialAbilities.Add(instance);
                    }
                }
                return specialAbilities;
            }
        
            public static void CallPendingAbilities(valheimmod Instance)
            {
                foreach (var ability in specialAbilities)
                {
                    ability?.CallPending(Instance);
                }
            }
            public static void CallSpecialAbilities()
            {
                foreach (var ability in specialAbilities)
                {
                    ability?.Call();
                }

            }
            public class Effects
            {
                public static List<StatusEffect> statusEffects = new List<StatusEffect>();
                public static List<StatusEffect> coolDownStatusEffects = new List<StatusEffect>();
                /// <summary>
                /// Register status effects for special abilities.
                /// </summary>
                public static void Register()
                {
                    foreach (var ability in specialAbilities)
                    {
                        ability.AddEffects();
                        List<StatusEffect> se = ability.GetStatusEffects();
                        List<StatusEffect> cdSE = ability.GetCooldownStatusEffects();
                        foreach (var effect in se)
                        {
                            statusEffects.Add(effect);
                        }
                        foreach (var effect in cdSE)
                        {
                            coolDownStatusEffects.Add(effect);
                        }
                    }
                }
                public static Dictionary<string, float> SavedOnDeath = new Dictionary<string, float>();
                public static void SaveOnDeath()
                {
                    foreach (StatusEffect effect in statusEffects)
                    {
                        float remainingTime = 0;
                        if (Player.m_localPlayer == null)
                        {
                            return;
                        }
                        if (Player.m_localPlayer.m_seman.HaveStatusEffect(effect.m_nameHash))
                        {
                            if (effect.name == "PendingTeleportEffect")
                            {
                                return;
                            }
                            if (effect.name == "TeleportEffect")
                            {
                                SavedOnDeath["effect_day"] = EnvMan.instance.GetDay(); // Save the current day for teleport effect
                                return; // Skip saving the teleport effect, as it is handled differently
                            }
                            remainingTime = Player.m_localPlayer.m_seman.GetStatusEffect(effect.m_nameHash).GetRemaningTime();
                        }
                        SavedOnDeath[effect.name] = remainingTime;
                    }
                }

                 public static void SaveToPreferencesOnDeath()
                {
                    Save();
                    foreach (var kvp in Saved)
                    {
                        string effectName = kvp.Key;
                        float remainingTime = kvp.Value;
                        Jotunn.Logger.LogInfo($"Saving to PlayerPrefs | {effectName} status effect with remaining time: {remainingTime}");
                        PlayerPrefs.SetFloat(effectName, (remainingTime > 0 && !float.IsNaN(remainingTime)) ? remainingTime : 0);
                    }
                    PlayerPrefs.Save();
                }

                public static Dictionary<string, float> Saved = new Dictionary<string, float>();
                public static void Save(bool onDeath = false)
                {
                    foreach (StatusEffect effect in statusEffects)
                    {
                        float remainingTime = 0;
                        if (Player.m_localPlayer == null)
                        {
                            return;
                        }
                        if (!onDeath)
                        {
                            if (Player.m_localPlayer.m_seman.HaveStatusEffect(effect.m_nameHash))
                            {
                                if (effect.name == "TeleportEffect" || effect.name == "PendingTeleportEffect")
                                {
                                    return; // Skip saving the teleport effect, as it is handled differently
                                }
                                remainingTime = Player.m_localPlayer.m_seman.GetStatusEffect(effect.m_nameHash).GetRemaningTime();
                            }
                            Saved[effect.name] = remainingTime;
                        }
                        else
                        {
                            if (Player.m_localPlayer.m_seman.HaveStatusEffect(effect.m_nameHash))
                            {
                                if (effect.name == "TeleportEffect" || effect.name == "PendingTeleportEffect")
                                {
                                    SavedOnDeath["effect_day"] =  EnvMan.instance.GetDay();; // Skip saving the teleport effect, as it is handled differently
                                }
                                remainingTime = Player.m_localPlayer.m_seman.GetStatusEffect(effect.m_nameHash).GetRemaningTime();
                            }
                            SavedOnDeath[effect.name] = remainingTime;
                        }
                    }
                }

                public static void SaveToPreferences(bool onDeath = false)
                {
                    var savedDict = onDeath ? SavedOnDeath : Saved;
                    if (onDeath)
                    {
                        SaveOnDeath();
                    }
                    else
                    {
                        Save();
                    }
                    Save();
                    foreach (var kvp in savedDict)
                    {
                        string effectName = kvp.Key;
                        float remainingTime = kvp.Value;
                        Jotunn.Logger.LogInfo($"Saving to PlayerPrefs | {effectName} status effect with remaining time: {remainingTime}");
                        PlayerPrefs.SetFloat(effectName, (remainingTime > 0 && !float.IsNaN(remainingTime)) ? remainingTime : 0);
                    }
                    PlayerPrefs.Save();
                }
                
                public static Dictionary<string, float> loadedStatusEffects = new Dictionary<string, float>();
                public static void Load(bool onDeath = false)
                {
                    foreach (SpecialAbilityBase ability in specialAbilities)
                    {
                        List<StatusEffect> abilityStatusEffects;
                        loadedStatusEffects.Clear();
                        // Load the status effect name and whether it is active
                        if (onDeath)
                            abilityStatusEffects = ability.GetCooldownStatusEffects();
                        else
                            abilityStatusEffects = ability.GetStatusEffects();
                        foreach (StatusEffect effect in abilityStatusEffects)
                        {
                            if (effect != null && effect.name != null)
                            {
                                float remainingTime = PlayerPrefs.GetFloat(effect.name);
                                if (remainingTime > 0)
                                {
                                    loadedStatusEffects[effect.name] = remainingTime;
                                }
                                if (effect.name == "TeleportEffect")
                                {
                                    Jotunn.Logger.LogInfo($"TeleportEffect loaded with remaining time: {remainingTime}");
                                    loadedStatusEffects["effect_day"] = PlayerPrefs.GetFloat("effect_day");
                                }
                                Jotunn.Logger.LogInfo($"Loaded {effect.name} status effect with remaining time: {remainingTime}");
                            }
                        }
                        Jotunn.Logger.LogInfo($"Loaded {ability.GetType().Name} status effects: {string.Join(", ", loadedStatusEffects.Select(kvp => $"{kvp.Key}: {kvp.Value}"))}");
                        ability.updateDuration(loadedStatusEffects);
                    }
                }

                public static void UpdateStatusEffect(Hud __instance, List<StatusEffect> statusEffectsList, Dictionary<string, string> durationDict = null)
                {
                    // Add null checks to prevent NullReferenceException
                    if (__instance == null || statusEffectsList == null || specialAbilities == null)
                        return;

                    // Update the textures for each status effect in the list
                    {
                        foreach (var ability in specialAbilities)
                        {
                            if (ability == null) continue;

                            for (int j = 0; j < statusEffectsList.Count; j++)
                            {
                                StatusEffect statusEffect = statusEffectsList[j];
                                if (statusEffect == null) continue;

                                try
                                {
                                    ability.updateTexture(__instance, statusEffect, j);
                                }
                                catch (System.Exception ex)
                                {
                                    Jotunn.Logger.LogError($"Error updating texture for ability {ability.GetType().Name}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }

            public abstract class SpecialAbilityBase
            {
                public virtual void Call() { }
                public virtual void CallPending(valheimmod instance = null) { }
                public abstract void AddEffects();
                public abstract List<StatusEffect> abilityStatusEffects { get; set; }  // Property to hold the status effects for the ability
                public abstract List<StatusEffect> abilityCooldownStatusEffects { get; set; }  // Property to hold the status effects for the ability
                public List<StatusEffect> GetStatusEffects()
                {
                    return abilityStatusEffects;
                }
                public List<StatusEffect> GetCooldownStatusEffects()
                {
                    return abilityCooldownStatusEffects;
                }
                /// <summary>
                /// Updates the durations of all the special ability effects
                public abstract void updateDuration(Dictionary<string, float> statusEffectDict); // mandatory method to update the duration of the ability
                public virtual void updateDurationByName(string name) { }
                public virtual void updateTexture(Hud __instance, StatusEffect statusEffect, int index) { }  // todo: remove the index thing and handle that differently for the one function
                protected virtual List<EffectList.EffectData> SetupEffectList() => null;
            }

            public class SpecialJump : SpecialAbilityBase
            {
                public Texture2D texture;
                public bool Triggered = false; // Flag to indicate if the special jump key is pressed down
                public int specialForce = 15; // Set the jump force for the special jump
                public int defaultForce = 8; // Set the default jump force
                public CustomStatusEffect SpecialEffect; // Custom status effect for the special jump
                public CustomStatusEffect PendingSpecialEffect; // Custom status effect for the special jump
                public override List<StatusEffect> abilityStatusEffects { get; set; } = new List<StatusEffect>();
                public override List<StatusEffect> abilityCooldownStatusEffects { get; set; } = new List<StatusEffect>();
                public static SpecialJump Instance = new SpecialJump();
                private static float cooldown = 5f * 60f;
                public override void CallPending(valheimmod instance = null)
                {
                    // If user picks the superjump buff in radial, give them the buff
                    RadialAbility radial_ability = GetRadialAbility();
                    string ability_name = radial_ability.ToString();
                    if (ability_name == RadialAbility.SuperJump.ToString())
                    {

                        if (!Player.m_localPlayer.m_seman.HaveStatusEffect(SpecialEffect.StatusEffect.m_nameHash))
                        {
                            Jotunn.Logger.LogInfo("Adding SpecialJump.PendingSpecialEffect status effect");
                            Player.m_localPlayer.m_seman.AddStatusEffect(PendingSpecialEffect.StatusEffect, true);
                        }
                    }
                }
                public override void Call()
                {
                    // if the player presses the jump button when they have the jump pending buff, give super jump effect

                    if (((ZInput.GetButton("Jump") || ZInput.GetButton("JoyJump")) && Player.m_localPlayer.m_seman.HaveStatusEffect(PendingSpecialEffect.StatusEffect.m_nameHash)))
                    {
                        Jotunn.Logger.LogInfo("Special jump button is pressed down");
                        Jotunn.Logger.LogInfo($"SpecialJump.PendingSpecialEffect StatusEffect Duration: {PendingSpecialEffect.StatusEffect.GetDuration()}");
                        Jotunn.Logger.LogInfo($"SpecialJump.PendingSpecialEffect StatusEffect IsDone: {PendingSpecialEffect.StatusEffect.IsDone()}");
                        if (Player.m_localPlayer.m_seman.HaveStatusEffect(PendingSpecialEffect.StatusEffect.m_nameHash) && !Player.m_localPlayer.m_seman.HaveStatusEffect(SpecialEffect.StatusEffect.m_nameHash))
                        {
                            Jotunn.Logger.LogInfo("Removing SpecialJump.PendingSpecialEffect status effect and adding JumpSpecialEffect status effect");
                            Player.m_localPlayer.m_seman.RemoveStatusEffect(PendingSpecialEffect.StatusEffect.m_nameHash, false);
                            Player.m_localPlayer.m_seman.AddStatusEffect(SpecialEffect.StatusEffect, false);
                            Triggered = true;
                            Jotunn.Logger.LogInfo($"SpecialJumpTriggered1 = {Triggered}");
                        }

                    }
                }

                public void Cancel()
                {
                    // If the player presses the jump button when they have the jump pending buff, give super jump effect
                }

                /// <summary>
                /// Sets up the sfx and vfx for the special jump ability.
                /// </summary>
                protected override List<EffectList.EffectData> SetupEffectList()
                {
                    GameObject frostPrefab = ZNetScene.instance.GetPrefab("vfx_Frost");
                    GameObject leafPuffPrefab = ZNetScene.instance.GetPrefab("vfx_bush_leaf_puff");
                    GameObject leafPuffHeathPrefab = ZNetScene.instance.GetPrefab("vfx_bush_leaf_puff_heath");
                    GameObject iceShardPrefab = ZNetScene.instance.GetPrefab("fx_iceshard_hit");
                    GameObject ghostDeathPrefab = ZNetScene.instance.GetPrefab("vfx_ghost_death");
                    GameObject soundPrefab = ZNetScene.instance.GetPrefab("sfx_Abomination_Attack2_slam_whoosh");
                    var effectList = new List<EffectList.EffectData>();

                    if (frostPrefab != null)
                    {
                        effectList.Add(new EffectList.EffectData
                        {
                            m_prefab = frostPrefab,
                            m_enabled = true,
                            m_attach = true
                        });
                    }
                    if (leafPuffPrefab != null)
                    {
                        effectList.Add(new EffectList.EffectData
                        {
                            m_prefab = leafPuffPrefab,
                            m_enabled = true,
                            m_attach = true,
                            m_follow = true,
                        });
                    }
                    if (leafPuffHeathPrefab != null)
                    {
                        effectList.Add(new EffectList.EffectData
                        {
                            m_prefab = leafPuffHeathPrefab,
                            m_enabled = true,
                            m_attach = true,
                            m_follow = true,
                        });
                    }
                    if (ghostDeathPrefab != null)
                    {
                        effectList.Add(new EffectList.EffectData
                        {
                            m_prefab = ghostDeathPrefab,
                            m_enabled = true,
                            m_attach = true,
                            m_follow = true,
                        });
                    }
                    if (iceShardPrefab != null)
                    {
                        effectList.Add(new EffectList.EffectData
                        {
                            m_prefab = iceShardPrefab,
                            m_enabled = true,
                            m_attach = true,
                            m_follow = true,
                        });
                    }

                    if (soundPrefab != null)
                    {
                        effectList.Add(new EffectList.EffectData
                        {
                            m_prefab = soundPrefab,
                            m_enabled = true,
                            m_attach = false,
                            m_follow = false,
                        });
                    }
                    return effectList;
                }

                public override void AddEffects()
                {
                    StatusEffect effect = ScriptableObject.CreateInstance<StatusEffect>();
                    StatusEffect pendeffect = ScriptableObject.CreateInstance<StatusEffect>();
                    effect.name = "SpecialJumpEffect";
                    effect.m_name = "$special_jumpeffect";
                    effect.m_tooltip = "$special_jumpeffect_tooltip";
                    effect.m_icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    effect.m_startMessageType = MessageHud.MessageType.Center;
                    //effect.m_startMessage = "$special_jumpeffect_start";
                    effect.m_stopMessageType = MessageHud.MessageType.Center;
                    effect.m_ttl = cooldown;
                    effect.m_cooldownIcon = effect.m_icon;
                    SpecialEffect = new CustomStatusEffect(effect, fixReference: false);

                    pendeffect.name = "PendingSpecialJumpEffect";
                    pendeffect.m_name = "$pending_special_jumpeffect";
                    pendeffect.m_tooltip = "$special_jumpeffect_tooltip";
                    pendeffect.m_icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    pendeffect.m_startMessageType = MessageHud.MessageType.Center;
                    pendeffect.m_startMessage = "$pending_special_jumpeffect_start";
                    pendeffect.m_stopMessageType = MessageHud.MessageType.Center;
                    pendeffect.m_stopMessage = "$pending_special_jumpeffect_stop";
                    pendeffect.m_ttl = 0f; // No TTL for pending effect

                    var effectList = SetupEffectList();
                    pendeffect.m_startEffects = new EffectList
                    {
                        m_effectPrefabs = effectList.ToArray()
                    };
                    PrefabManager.OnPrefabsRegistered -= ModAbilities.Effects.Register;
                    PendingSpecialEffect = new CustomStatusEffect(pendeffect, fixReference: false);
                    abilityStatusEffects.Add(SpecialEffect.StatusEffect);
                    abilityCooldownStatusEffects.Add(SpecialEffect.StatusEffect);
                    abilityStatusEffects.Add(PendingSpecialEffect.StatusEffect);
                }
                public override void updateDuration(Dictionary<string, float> statusEffectDict)
                {
                    foreach (var kvp in statusEffectDict)
                    {
                        string effectName = kvp.Key;
                        float remainingTime = kvp.Value;

                        if (effectName == SpecialEffect.StatusEffect.name)
                        {
                            Jotunn.Logger.LogInfo($"Updating Special Jump effect duration: {effectName} with remaining time: {remainingTime}");
                            SpecialEffect.StatusEffect.m_ttl = remainingTime;
                            Player.m_localPlayer.m_seman.AddStatusEffect(SpecialEffect.StatusEffect, true);
                        }
                        SpecialEffect.StatusEffect.m_ttl = cooldown;

                    }
                }

            }
            /// <summary>
            /// Handles the special teleport ability, including adding effects and managing the teleport countdown.
            /// </summary>
            public class SpecialTeleport : SpecialAbilityBase
            {
                public Texture2D texture;
                public CustomStatusEffect PendingSpecialEffect; // Custom status effect for the teleport home pending state
                public CustomStatusEffect SpecialEffect; // Custom status effect for the teleport home
                public bool teleportCancelled = false;
                public bool teleportPending = false;
                public string teleportEndingMsg = "Traveling...";
                public override List<StatusEffect> abilityStatusEffects { get; set; } = new List<StatusEffect>();
                public override List<StatusEffect> abilityCooldownStatusEffects { get; set; } = new List<StatusEffect>();
                public static SpecialTeleport Instance = new SpecialTeleport();

                public override void Call()
                {
                    // no need to implement as we do it all in CallPending()
                    return;
                }
                public override void CallPending(valheimmod Instance)
                {
                    // If user picks the teleport home ability in radial, teleport them home
                    RadialAbility radial_ability = GetRadialAbility();
                    string ability_name = radial_ability.ToString();
                    if (ability_name == RadialAbility.TeleportHome.ToString())
                    {
                        if (Player.m_localPlayer.m_seman.HaveStatusEffect(SpecialEffect.StatusEffect.m_nameHash))
                        {
                            Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$teleporteffect_cd");
                            return;
                        }
                        if (!Player.m_localPlayer.m_seman.HaveStatusEffect(PendingSpecialEffect.StatusEffect.m_nameHash))
                        {
                            teleportCancelled = false;
                            teleportPending = true;
                            Jotunn.Logger.LogInfo("Adding TeleportHomeSpecialEffect status effect");
                            Player.m_localPlayer.m_seman.AddStatusEffect(PendingSpecialEffect.StatusEffect, true);
                            Instance.StartTeleportCountdown(10);

                        }
                        else
                        {
                            Cancel();
                        }
                    }
                }

                public void Cancel()
                {
                    if (valheimmod.Instance.teleportCountdownCoroutine != null)
                    {
                        valheimmod.Instance.StopCoroutine(valheimmod.Instance.teleportCountdownCoroutine);
                        valheimmod.Instance.teleportCountdownCoroutine = null;
                        if (Player.m_localPlayer != null)
                        {
                            teleportCancelled = true;
                            teleportPending = false;
                            Player.m_localPlayer.m_seman.RemoveStatusEffect(PendingSpecialEffect.StatusEffect.m_nameHash, false);
                        }
                    }
                }
                public override void AddEffects()
                {
                    StatusEffect pendteleporteffect = ScriptableObject.CreateInstance<StatusEffect>();
                    StatusEffect teleporteffect = ScriptableObject.CreateInstance<StatusEffect>();
                    pendteleporteffect.name = "PendingTeleportEffect";
                    pendteleporteffect.m_name = "$pending_teleport_effect";
                    pendteleporteffect.m_tooltip = "$special_teleport_tooltip";
                    pendteleporteffect.m_icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    pendteleporteffect.m_startMessageType = MessageHud.MessageType.Center;
                    pendteleporteffect.m_startMessage = "$pending_teleporteffect_start";
                    pendteleporteffect.m_stopMessageType = MessageHud.MessageType.Center;
                    pendteleporteffect.m_ttl = 0f; // No TTL for pending effect
                    teleporteffect.name = "TeleportEffect";
                    teleporteffect.m_name = "$teleport_effect";
                    teleporteffect.m_tooltip = "$special_teleport_cd_tooltip";
                    teleporteffect.m_icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    teleporteffect.m_startMessageType = MessageHud.MessageType.Center;
                    teleporteffect.m_startMessage = "$teleporteffect_start";
                    teleporteffect.m_stopMessageType = MessageHud.MessageType.Center;
                    teleporteffect.m_stopMessage = "$teleporteffect_stop";
                    teleporteffect.m_ttl = 0f;
                    teleporteffect.m_cooldownIcon = teleporteffect.m_icon;

                    PendingSpecialEffect = new CustomStatusEffect(pendteleporteffect, fixReference: false);
                    SpecialEffect = new CustomStatusEffect(teleporteffect, fixReference: false);
                    abilityStatusEffects.Add(SpecialEffect.StatusEffect);
                    abilityCooldownStatusEffects.Add(SpecialEffect.StatusEffect);
                    abilityStatusEffects.Add(PendingSpecialEffect.StatusEffect);
                }

                public override void updateDuration(Dictionary<string, float> statusEffectDict)
                {
                    foreach (var kvp in statusEffectDict)
                    {
                        string effectName = kvp.Key;
                        float day = kvp.Value;

                        if (effectName == "effect_day")
                        {
                            currentDay = EnvMan.instance.GetDay();
                            Jotunn.Logger.LogInfo($"Teleport effect day: {day}, current day {currentDay}");
                            // don't re-add the duration if there is a new day since last
                            if (currentDay <= day)
                            {
                                Jotunn.Logger.LogInfo($"Updating Teleport effect duration");
                                Player.m_localPlayer.m_seman.AddStatusEffect(SpecialEffect.StatusEffect, true);
                            }
                                // don't set a time update as the ttl is handled by the game days
                        }

                    }
                }

                public System.Collections.IEnumerator TeleportCountdownCoroutine(int seconds)
                {
                    for (int i = seconds; i > 0; i--)
                    {
                        if (Player.m_localPlayer != null)
                        {
                            Player.m_localPlayer.Message(MessageHud.MessageType.Center, $"Teleporting home in {i}...");
                        }
                        yield return new WaitForSeconds(1f);

                        // Optional: If teleport was cancelled during countdown, exit early
                        if (teleportCancelled || !teleportPending)
                        {
                            valheimmod.Instance.teleportCountdownCoroutine = null;
                            yield break;
                        }
                    }
                    valheimmod.Instance.teleportCountdownCoroutine = null;

                    // Only run this if teleport wasn't cancelled
                    if (!teleportCancelled && teleportPending)
                    {
                        // Place your post-countdown logic here
                        Player.m_localPlayer.m_seman.AddStatusEffect(SpecialEffect.StatusEffect, true);
                        PlayerProfile profile = Game.instance.GetPlayerProfile();
                        Vector3 homepoint = profile.GetCustomSpawnPoint(); // Get the player's home point
                        if (homepoint == Vector3.zero)
                        {
                            Player.m_localPlayer.Message(MessageHud.MessageType.Center, "You don't have a bed. Teleporting to Sacrificial Stones");
                            homepoint = profile.GetHomePoint(); // Fallback to the default home point
                        }
                        Player.m_localPlayer.TeleportTo(homepoint, Quaternion.identity, true); // TelepoSetCustomSpawnPointrt the player to their home point
                        Player.m_localPlayer.m_seman.RemoveStatusEffect(PendingSpecialEffect.StatusEffect.m_nameHash, false); // Remove the pending teleport effect
                        // save the last day the teleport was used, this is used to persist the CD effect through logout
                        Effects.Saved["effect_day"] = EnvMan.instance.GetDay();
                        ModAbilities.Effects.SaveToPreferences();
                        teleportPending = false;
                    }
                }
            }


            // todo: fix issues with player bow level not reverting on death
            // and on logout
            public class SpectralArrow : SpecialAbilityBase
            {
                public Texture2D texture;
                public Sprite[] textures = new Sprite[3];
                public CustomStatusEffect SpecialEffect;
                public CustomStatusEffect SpecialCDEffect;
                public override List<StatusEffect> abilityStatusEffects { get; set; } = new List<StatusEffect>();
                public override List<StatusEffect> abilityCooldownStatusEffects { get; set; } = new List<StatusEffect>();
                public Dictionary<Player, int> ShotsFired = new Dictionary<Player, int>();
                public Dictionary<Player, float> PreviousSkill = new Dictionary<Player, float>();
                public float specialVelocity = 30f; // base velocity for the spectral arrow
                public float specialDrawDurationMin = 0.1f;
                // public ItemDrop.ItemData weapon;
                public List<ItemDrop.ItemData> weaponList = new List<ItemDrop.ItemData>(); // List of weapons to apply the spectral arrow effect to
                public Dictionary<string, (float velocity, float drawTime)> weaponDefaults = new Dictionary<string, (float, float)>(); // Store default weapon velocities and draw times
                internal float cooldown = 60f * 10f; // cooldown time for the spectral arrow ability
                public static SpectralArrow Instance = new SpectralArrow();

                public void RestoreWeaponDefaults()
                {
                    foreach (ItemDrop.ItemData weapon in weaponList)
                    {
                        if (weaponDefaults.ContainsKey(weapon.m_shared.m_name))
                        {
                            var defaults = weaponDefaults[weapon.m_shared.m_name];
                            weapon.m_shared.m_attack.m_projectileVel = defaults.velocity;
                            weapon.m_shared.m_attack.m_drawDurationMin = defaults.drawTime;
                            Jotunn.Logger.LogInfo($"Spectral Arrow: Restored velocity for {weapon.m_shared.m_name} to {defaults.velocity} and draw time to {defaults.drawTime}");
                        }
                    }
                }
                public override void Call()
                {
                    return;
                }
                public override void CallPending(valheimmod instance = null)
                {
                    if (Player.m_localPlayer == null)
                    {
                        return;
                    }
                    // If user picks the spectral arrow ability in radial, give them the buff
                    RadialAbility radial_ability = GetRadialAbility();
                    string ability_name = radial_ability.ToString();
                    if (ability_name == RadialAbility.SpectralArrow.ToString())
                    {
                        if (!Player.m_localPlayer.m_seman.HaveStatusEffect(SpecialCDEffect.StatusEffect.m_nameHash) && !Player.m_localPlayer.m_seman.HaveStatusEffect(SpecialEffect.StatusEffect.m_nameHash))
                        {
                            // If the player doesn't have the spectral arrow effect or the pending effect, add the pending effect
                            Jotunn.Logger.LogInfo("Spectral Arrow ability selected, adding PendingSpectralArrowEffect status effect");
                            {
                                Jotunn.Logger.LogInfo("Adding PendingSpectralArrowEffect status effect");
                                Player.m_localPlayer.m_seman.AddStatusEffect(SpecialEffect.StatusEffect, true);
                            }
                        }
                    }
                }
                public void Cancel(Player __instance)
                {
                    /// <summary>
                    /// Cancels the spectral arrow ability, removing the status effects and resetting the weapon projectile velocity.
                    /// Does not allow the player to cancel from radial menu, only from the status effect.
                    /// /// </summary>
                    if (__instance != null)
                    {
                        if (SpecialEffect == null)
                        {
                            Jotunn.Logger.LogError("SpectralArrow SpecialEffect or SpecialCDEffect is null, cannot cancel ability.");
                            return;
                        }
                        if (__instance.m_seman.HaveStatusEffect(SpecialEffect.StatusEffect.m_nameHash))
                        {
                            Jotunn.Logger.LogInfo("Removing SpectralArrowEffect status effect and adding SpectralArrowCDEffect status effect");
                            __instance.m_seman.RemoveStatusEffect(SpecialEffect.StatusEffect.m_nameHash, false);
                            __instance.m_seman.AddStatusEffect(SpecialCDEffect.StatusEffect, false);
                        }
                        // remove the skill and reset the shots fired
                        ShotsFired.Remove(Player.m_localPlayer);
                        RestoreWeaponDefaults();
                    }
                }
                public override void AddEffects()
                {
                    StatusEffect effect = ScriptableObject.CreateInstance<StatusEffect>();
                    StatusEffect pendeffect = ScriptableObject.CreateInstance<StatusEffect>();
                    pendeffect.name = "SpectralArrowEffect";
                    pendeffect.m_name = "$spectral_arrow_effect";
                    pendeffect.m_tooltip = "$special_spectral_arrow_tooltip";
                    pendeffect.m_icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    pendeffect.m_startMessageType = MessageHud.MessageType.TopLeft;
                    pendeffect.m_startMessage = "$spectral_arrow_start";
                    pendeffect.m_stopMessageType = MessageHud.MessageType.TopLeft;
                    pendeffect.m_ttl = 0f; // No TTL for pending effect
                    effect.name = "SpectralArrowCDEffect";
                    effect.m_name = "$spectral_arrow_effect";
                    effect.m_tooltip = "$spectral_arrow_cd_tooltip";
                    effect.m_icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    effect.m_startMessageType = MessageHud.MessageType.TopLeft;
                    effect.m_startMessage = "$spectral_arrow_cd_start";
                    effect.m_stopMessageType = MessageHud.MessageType.TopLeft;
                    effect.m_stopMessage = "$spectral_arrow_cd_stop";
                    effect.m_ttl = cooldown; // 10 minutes cooldown
                    effect.m_cooldownIcon = effect.m_icon;

                    SpecialEffect = new CustomStatusEffect(pendeffect, fixReference: false);
                    SpecialCDEffect = new CustomStatusEffect(effect, fixReference: false);
                    abilityStatusEffects.Add(SpecialEffect.StatusEffect);
                    abilityCooldownStatusEffects.Add(SpecialCDEffect.StatusEffect);
                    abilityStatusEffects.Add(SpecialCDEffect.StatusEffect);
                }
                public override void updateDuration(Dictionary<string, float> statusEffectDict)
                {
                    foreach (var kvp in statusEffectDict)
                    {
                        string effectName = kvp.Key;
                        float remainingTime = kvp.Value;

                        if (effectName == SpecialCDEffect.StatusEffect.name)
                        {
                            Jotunn.Logger.LogInfo($"Updating SpectralArrow effect duration: {effectName} with remaining time: {remainingTime}");
                            SpecialCDEffect.StatusEffect.m_ttl = remainingTime;
                            Player.m_localPlayer.m_seman.AddStatusEffect(SpecialCDEffect.StatusEffect, false);
                        }
                        SpecialCDEffect.StatusEffect.m_ttl = cooldown;
                        Jotunn.Logger.LogInfo($"SpecialCDEffect Spectral Arrow: {SpecialCDEffect.StatusEffect.m_ttl}");
                    }

                }
                public override void updateTexture(Hud __instance, StatusEffect statusEffect, int index)
                {
                    // Add null checks to prevent NullReferenceException
                    if (statusEffect?.m_name == null || SpecialEffect?.StatusEffect?.m_name == null)
                        return;

                    if (Player.m_localPlayer == null)
                        return;

                    if (statusEffect.m_name == SpecialEffect.StatusEffect.m_name)
                    {
                        // Find the correct icon for the current arrow count
                        int arrowsLeft = 3 - (ShotsFired.ContainsKey(Player.m_localPlayer) ? ShotsFired[Player.m_localPlayer] : 0);
                        if (arrowsLeft > 0 && arrowsLeft <= 3)
                        {
                            // Add null checks for HUD components
                            if (__instance?.m_statusEffects == null || index < 0 || index >= __instance.m_statusEffects.Count)
                                return;

                            // Update the icon in the HUD
                            RectTransform val2 = __instance.m_statusEffects[index];
                            if (val2 == null)
                                return;

                            Transform iconTransform = ((Transform)val2).Find("Icon");
                            if (iconTransform == null)
                                return;

                            Image component = iconTransform.GetComponent<Image>();
                            if (component != null && textures != null && arrowsLeft - 1 < textures.Length)
                            {
                                component.sprite = textures[arrowsLeft - 1];
                            }
                        }
                    }
                }
            }

            public class ValhallaDome : SpecialAbilityBase
            {
                public Texture2D texture;
                public GameObject ActiveDome;
                public string LastDomeUID;
                public string dome_uid = "valhalladome_uid";
                public CustomStatusEffect SpecialEffect;
                public CustomStatusEffect SpecialCDEffect;
                public override List<StatusEffect> abilityStatusEffects { get; set; } = new List<StatusEffect>();
                public override List<StatusEffect> abilityCooldownStatusEffects { get; set; } = new List<StatusEffect>();
                public bool abilityUsed = false; // Flag to indicate if the ability has been used
                internal float ttl = 30f; // Time before the dome is destroyed
                internal float cooldown = 15f * 60f; // Time before ability can be used again
                public static ValhallaDome Instance = new ValhallaDome();
                public override void Call()
                {
                    return;
                }
                public void CallManual()
                {
                    if (Player.m_localPlayer == null) return;
                    GameObject domePrefab = ZNetScene.instance.GetPrefab("piece_shieldgenerator");
                    if (domePrefab != null)
                    {
                        Vector3 pos = Player.m_localPlayer.transform.position;
                        Quaternion rot = Quaternion.identity;
                        ActiveDome = UnityEngine.Object.Instantiate(domePrefab, pos, rot);
                        var znetView = ActiveDome.GetComponent<ZNetView>();
                        if (znetView != null && znetView.IsValid())
                        {
                            string uniqueId = System.Guid.NewGuid().ToString();
                            znetView.GetZDO().Set(dome_uid, uniqueId);
                            // Save this somewhere (e.g.,  field) for later lookup
                            Instance.LastDomeUID = uniqueId;
                            PlayerPrefs.SetString("Dome_LastDomeUID", uniqueId);
                            PlayerPrefs.Save();

                            Jotunn.Logger.LogInfo($"Dome created with UID: {uniqueId}");
                        }
                        // Start a coroutine to set up the shield after one frame
                        Player.m_localPlayer.StartCoroutine(SetupShieldNextFrame(ActiveDome));
                    }
                }
                public override void AddEffects()
                {
                    StatusEffect cddomeeffect = ScriptableObject.CreateInstance<StatusEffect>();
                    StatusEffect domeeffect = ScriptableObject.CreateInstance<StatusEffect>();
                    cddomeeffect.name = "CDDomeEffect";
                    cddomeeffect.m_name = "$cd_dome_effect";
                    cddomeeffect.m_tooltip = "$cd_dome_tooltip";
                    cddomeeffect.m_icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    cddomeeffect.m_startMessageType = MessageHud.MessageType.Center;
                    cddomeeffect.m_startMessage = "$cd_domeeffect_start";
                    cddomeeffect.m_stopMessageType = MessageHud.MessageType.Center;
                    cddomeeffect.m_ttl = cooldown;
                    cddomeeffect.m_cooldownIcon = cddomeeffect.m_icon;
                    domeeffect.name = "DomeEffect";
                    domeeffect.m_name = "$dome_effect";
                    domeeffect.m_tooltip = "$dome_tooltip";
                    domeeffect.m_icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    domeeffect.m_startMessageType = MessageHud.MessageType.Center;
                    domeeffect.m_startMessage = "$domeeffect_start";
                    domeeffect.m_stopMessageType = MessageHud.MessageType.Center;
                    domeeffect.m_stopMessage = "$domeeffect_stop";
                    domeeffect.m_ttl = ttl;

                    SpecialCDEffect = new CustomStatusEffect(cddomeeffect, fixReference: false);
                    SpecialEffect = new CustomStatusEffect(domeeffect, fixReference: false);
                    abilityStatusEffects.Add(SpecialEffect.StatusEffect);
                    abilityStatusEffects.Add(SpecialCDEffect.StatusEffect);
                    abilityCooldownStatusEffects.Add(SpecialCDEffect.StatusEffect);
                }

                public override void updateDuration(Dictionary<string, float> statusEffectDict)
                {
                    foreach (var kvp in statusEffectDict)
                    {
                        string effectName = kvp.Key;
                        float remainingTime = kvp.Value;

                        if (effectName == SpecialCDEffect.StatusEffect.name)
                        {
                            Jotunn.Logger.LogInfo($"Updating ValhallaDome effect duration: {effectName} with remaining time: {remainingTime}");
                            SpecialCDEffect.StatusEffect.m_ttl = remainingTime;
                            Player.m_localPlayer.m_seman.AddStatusEffect(SpecialCDEffect.StatusEffect, false);
                        }
                        SpecialCDEffect.StatusEffect.m_ttl = cooldown;
                        Jotunn.Logger.LogInfo($"SpecialCDEffect Valhalla Dome : {SpecialCDEffect.StatusEffect.m_ttl}");
                    }

                }
                public class MobOnlyShield : MonoBehaviour
                {
                    private float repelForce = 30f; // Adjust this value to change the force applied to mobs
                    
                    private void OnTriggerEnter(Collider other)
                    {
                        Jotunn.Logger.LogInfo($"MobOnlyShield OnTriggerEnter called for {other.name}");
                        HandleMobRepelling(other);
                    }
                    
                    private void OnTriggerStay(Collider other)
                    {
                        HandleMobRepelling(other, true);
                    }
                    
                    private void HandleMobRepelling(Collider other, bool isStay = false)
                    {
                        Character character = other.GetComponent<Character>();
                        if (character == null) return;
                        
                        // Check if it's a monster/hostile creature
                        if (character.IsMonsterFaction(0f))
                        {
                            if (!isStay)
                                Jotunn.Logger.LogInfo($"Repelling mob: {character.name}");
                            
                            // Only apply force if we can (either local player or have authority)
                            if (character.m_nview != null && (character.m_nview.IsOwner() || Player.m_localPlayer != null))
                            {
                                Vector3 repelDir = (character.transform.position - transform.position).normalized;
                                if (character.m_body != null)
                                {
                                    character.m_body.AddForce(repelDir * repelForce, ForceMode.VelocityChange);
                                }
                                else
                                {
                                    // Fallback: directly modify position if no rigidbody
                                    character.transform.position += repelDir * 0.1f;
                                }
                            }
                        }
                        else if (character != null && !isStay)
                        {
                            Jotunn.Logger.LogInfo($"Ignoring non-monster character: {character.name}");
                        }
                    }
                }
                private IEnumerator SetupShieldNextFrame(GameObject dome)
                {
                    yield return null; // Wait one frame

                    var shieldGen = dome.GetComponent<ShieldGenerator>();
                    if (shieldGen != null)
                    {
                        shieldGen.m_offWhenNoFuel = false;
                        shieldGen.m_minShieldRadius = 4f;
                        shieldGen.m_maxShieldRadius = 4f; // Set the desired shield radius
                        shieldGen.SetFuel(shieldGen.m_maxFuel);
                        shieldGen.UpdateShield();

                        // Hide all MeshRenderers except the dome
                        foreach (var renderer in dome.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            if (shieldGen.m_shieldDome == null || !renderer.transform.IsChildOf(shieldGen.m_shieldDome.transform))
                            {
                                renderer.enabled = false;
                            }
                        }
                        // Add a collider to the dome mesh if not present
                        var domeObj = shieldGen.m_shieldDome;
                        // Remove unwanted components for visuals only
                        UnityEngine.Object.Destroy(dome.GetComponent<Collider>());
                        foreach (var col in dome.GetComponentsInChildren<Collider>())
                        {
                            UnityEngine.Object.Destroy(col);
                        }
                        if (domeObj != null)
                        {
                            var collider = domeObj.GetComponent<SphereCollider>();
                            if (collider == null)
                            {
                                collider = domeObj.AddComponent<SphereCollider>();
                                collider.isTrigger = true; // Use trigger for custom logic
                                float visualRadius = shieldGen.m_maxShieldRadius;
                                Jotunn.Logger.LogInfo($"Dome scale: {domeObj.transform.lossyScale}, collider.radius: {collider.radius}, visualRadius: {visualRadius}");
                                float scale = domeObj.transform.lossyScale.x; // Use .x, .y, or .z if non-uniform
                                float fudge = 0.3f; // Adjust this value to change the size of the collider relative to the visual radius
                                collider.radius = (visualRadius / scale) * fudge; // Adjust radius based on the scale of the dome
                                domeObj.AddComponent<MobOnlyShield>();
                            }
                            // Set to a custom layer (make sure this layer exists and is set up in Unity)
                            domeObj.layer = LayerMask.NameToLayer("character");
                        }
                        else
                        {
                            Jotunn.Logger.LogWarning("ShieldGenerator component not found on the instantiated dome!");
                        }

                        var timedDestruction = ActiveDome.GetComponent<TimedDestruction>();
                        if (timedDestruction == null)
                        {
                            timedDestruction = ActiveDome.AddComponent<TimedDestruction>();
                            Jotunn.Logger.LogInfo("Added TimedDestruction component to ValhallaDome");
                        }
                        timedDestruction.m_forceTakeOwnershipAndDestroy = true;
                        timedDestruction.m_timeout = ttl; // time before destruction
                        timedDestruction.Trigger();
                        Jotunn.Logger.LogInfo("Set m_forceTakeOwnershipAndDestroy = true on TimedDestruction");
                    }
                }
                public override void CallPending(valheimmod instance = null)
                {
                    // If user picks the valhalla dome ability in radial, give them the buff
                    RadialAbility radial_ability = GetRadialAbility();
                    string ability_name = radial_ability.ToString();
                    if (ability_name == RadialAbility.ValhallaDome.ToString())
                    {
                        if (Player.m_localPlayer.m_seman.HaveStatusEffect(SpecialEffect.StatusEffect.m_nameHash) || Player.m_localPlayer.m_seman.HaveStatusEffect(SpecialCDEffect.StatusEffect.m_nameHash))
                        {
                            return;
                        }
                        
                        // Try to create the dome first, only add status effect if successful
                        bool domeCreated = TryCreateDome();
                        if (domeCreated)
                        {
                            // Add the status effect only after successful dome creation
                            Player.m_localPlayer.m_seman.AddStatusEffect(SpecialEffect.StatusEffect, true);
                            ValhallaDome.Instance.abilityUsed = true;
                        }
                        else
                        {
                            // If dome creation failed, show a message but don't add cooldown
                            Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Failed to create dome");
                        }
                    }
                }
                
                /// <summary>
                /// Try to create a dome, return true if successful
                /// </summary>
                private bool TryCreateDome()
                {
                    try
                    {
                        Jotunn.Logger.LogInfo("ValhallaDome: TryCreateDome called");
                        Vector3 position = Player.m_localPlayer.transform.position;
                        
                        // For now, use the simple approach like the original CallManual
                        // The networking can be added back later once this works
                        if (Player.m_localPlayer == null) 
                        {
                            Jotunn.Logger.LogError("ValhallaDome: Player.m_localPlayer is null");
                            return false;
                        }
                        
                        if (ZNetScene.instance == null)
                        {
                            Jotunn.Logger.LogError("ValhallaDome: ZNetScene.instance is null");
                            return false;
                        }
                        
                        GameObject domePrefab = ZNetScene.instance.GetPrefab("piece_shieldgenerator");
                        if (domePrefab != null)
                        {
                            Jotunn.Logger.LogInfo("ValhallaDome: Found shield generator prefab, creating dome");
                            Quaternion rot = Quaternion.identity;
                            ActiveDome = UnityEngine.Object.Instantiate(domePrefab, position, rot);
                            Jotunn.Logger.LogInfo($"ValhallaDome: Instantiated dome at position {position}");
                            
                            var znetView = ActiveDome.GetComponent<ZNetView>();
                            if (znetView != null && znetView.IsValid())
                            {
                                Jotunn.Logger.LogInfo("ValhallaDome: ZNetView is valid, setting up dome");
                                string uniqueId = System.Guid.NewGuid().ToString();
                                znetView.GetZDO().Set(dome_uid, uniqueId);
                                znetView.GetZDO().Set("valhalla_dome_setup", true); // Mark this as a valhalla dome
                                
                                Instance.LastDomeUID = uniqueId;
                                PlayerPrefs.SetString("Dome_LastDomeUID", uniqueId);
                                PlayerPrefs.Save();

                                Jotunn.Logger.LogInfo($"ValhallaDome: Dome created with UID: {uniqueId}");
                                
                                // Start a coroutine to set up the shield after one frame
                                Player.m_localPlayer.StartCoroutine(SetupShieldNextFrame(ActiveDome));
                                return true;
                            }
                            else
                            {
                                Jotunn.Logger.LogError("ValhallaDome: ZNetView is null or invalid");
                                if (znetView == null)
                                    Jotunn.Logger.LogError("ValhallaDome: ZNetView component not found");
                                else
                                    Jotunn.Logger.LogError("ValhallaDome: ZNetView is not valid");
                                return false;
                            }
                        }
                        else
                        {
                            Jotunn.Logger.LogError("ValhallaDome: Could not find piece_shieldgenerator prefab");
                            return false;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Jotunn.Logger.LogError($"ValhallaDome: Error creating dome: {ex.Message}");
                        return false;
                    }
                }
                
                /// <summary>
                /// Request dome creation at the specified position. Handles both server and client cases.
                /// </summary>
                public void RequestDomeCreation(Vector3 position)
                {
                    try
                    {
                        if (ZNet.instance == null)
                        {
                            Jotunn.Logger.LogError("ValhallaDome: ZNet.instance is null, cannot create dome");
                            return;
                        }
                        
                        if (ZNet.instance.IsServer())
                        {
                            // If we're the server, create the dome directly
                            Jotunn.Logger.LogInfo("ValhallaDome: Server creating dome directly");
                            CreateDomeAtPosition(position);
                        }
                        else
                        {
                            // If we're a client, send an RPC request to the server
                            Jotunn.Logger.LogInfo("ValhallaDome: Client requesting dome creation from server");
                            if (ZRoutedRpc.instance != null && ZNet.instance.GetServerPeer() != null)
                            {
                                ZRoutedRpc.instance.InvokeRoutedRPC(ZNet.instance.GetServerPeer().m_uid, "ValhallaDome_RequestCreation", 
                                    position.x, position.y, position.z);
                            }
                            else
                            {
                                Jotunn.Logger.LogError("ValhallaDome: Cannot send RPC - ZRoutedRpc or server peer is null");
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Jotunn.Logger.LogError($"ValhallaDome: Error in RequestDomeCreation: {ex.Message}");
                    }
                }
                
                /// <summary>
                /// Actually creates the dome at the specified position. Only called on server.
                /// </summary>
                public void CreateDomeAtPosition(Vector3 position)
                {
                    try
                    {
                        if (ZNet.instance == null || !ZNet.instance.IsServer())
                        {
                            Jotunn.Logger.LogWarning("ValhallaDome: CreateDomeAtPosition called on client, ignoring");
                            return;
                        }
                        
                        GameObject domePrefab = ZNetScene.instance?.GetPrefab("piece_shieldgenerator");
                        if (domePrefab != null)
                        {
                            Quaternion rot = Quaternion.identity;
                            
                            // Create the dome through the network spawning system
                            GameObject spawnedDome = UnityEngine.Object.Instantiate(domePrefab, position, rot);
                            var znetView = spawnedDome.GetComponent<ZNetView>();
                            
                            if (znetView != null)
                            {
                                // Take ownership of the networked object
                                znetView.ClaimOwnership();
                                
                                if (znetView.IsValid())
                                {
                                    string uniqueId = System.Guid.NewGuid().ToString();
                                    znetView.GetZDO().Set(dome_uid, uniqueId);
                                    znetView.GetZDO().Set("valhalla_dome_setup", true); // Mark this as a valhalla dome
                                    
                                    Instance.LastDomeUID = uniqueId;
                                    PlayerPrefs.SetString("Dome_LastDomeUID", uniqueId);
                                    PlayerPrefs.Save();

                                    Jotunn.Logger.LogInfo($"ValhallaDome: Server created dome with UID: {uniqueId} at position {position}");
                                    
                                    // Store reference locally for the server
                                    if (ActiveDome == null) // Only set if we don't have an active dome
                                    {
                                        ActiveDome = spawnedDome;
                                    }
                                    
                                    // Setup the dome immediately for the server/host
                                    if (Player.m_localPlayer != null)
                                    {
                                        Player.m_localPlayer.StartCoroutine(SetupShieldNextFrame(spawnedDome));
                                    }
                                }
                                else
                                {
                                    Jotunn.Logger.LogError("ValhallaDome: ZNetView is not valid, cannot create dome");
                                }
                            }
                            else
                            {
                                Jotunn.Logger.LogError("ValhallaDome: No ZNetView component found on dome prefab");
                            }
                        }
                        else
                        {
                            Jotunn.Logger.LogError("ValhallaDome: Could not find piece_shieldgenerator prefab");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Jotunn.Logger.LogError($"ValhallaDome: Error in CreateDomeAtPosition: {ex.Message}");
                    }
                }

                public void OnPlayerLogout()
                {
                    Jotunn.Logger.LogInfo($"OnPlayerLogout called. Dome ref: {ActiveDome}");
                    if (ActiveDome != null)
                    {
                        var timedDestruction = ActiveDome.GetComponent<TimedDestruction>();
                        if (timedDestruction == null)
                        {
                            timedDestruction = ActiveDome.AddComponent<TimedDestruction>();
                            Jotunn.Logger.LogInfo("Added TimedDestruction component to ValhallaDome");
                        }
                        else
                        {
                            Jotunn.Logger.LogInfo("TimedDestruction component already exists on ValhallaDome");
                        }
                        timedDestruction.m_forceTakeOwnershipAndDestroy = true;
                        timedDestruction.m_timeout = 0f; // or your desired time
                        timedDestruction.Trigger();
                        timedDestruction.DestroyNow();
                        Jotunn.Logger.LogInfo("Set m_forceTakeOwnershipAndDestroy = true on TimedDestruction");
                    }
                    else
                    {
                        Jotunn.Logger.LogInfo("No active Dome to destroy.");
                    }
                }
            }
        }
    }
}

