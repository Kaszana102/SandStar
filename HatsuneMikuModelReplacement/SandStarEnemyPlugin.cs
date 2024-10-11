using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BepInEx.Configuration;
using BepInEx;
using Unity;
using UnityEngine;
using System.IO;
using System.Reflection;
using HarmonyLib;
using BepInEx.Logging;
using LethalLib.Modules;


namespace SandStarEnemy
{
    [BepInPlugin("SandStarEnemy", "SandStarEnemy", "1.0.0")]    
    public class SandStarEnemyPlugin: BaseUnityPlugin
    {
        public static EnemyType EnemyType;
        internal static ManualLogSource Log;

        const string EnemyTypePath = "Assets/SandStar/SandStarType.asset";
        private void Awake()
        {
            Log = Logger;            
            LoadAssets();
            Logger.LogInfo($"Plugin {"Sand Star enemy"} PROPERLY LOADED!");
        }

        private void LoadAssets()
        {
            Assets.PopulateAssets();
            LoadNetWeaver();            
            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());

            /*foreach (string name in Assets.MainAssetBundle.GetAllAssetNames())
            {
                Logger.LogInfo($"Plugin {"SandStar"} :" + name);
            }*/


            if (Assets.MainAssetBundle.Contains(EnemyTypePath))
            {                
                
                EnemyType = Assets.MainAssetBundle.LoadAsset<EnemyType>(EnemyTypePath);

                if (EnemyType == null)
                {
                    Logger.LogInfo($"Plugin {"SandStar"} ENEMYTYPE IS NULL!");                    
                }
                else
                {
                    if(EnemyType.enemyPrefab == null)
                    {
                        Logger.LogInfo($"Plugin {"SandStar"} SandStar ENEMY PREFAB MISSING!");
                    }
                    else
                    {
                        Logger.LogInfo($"Plugin {"SandStar"} SandStar ENEMY TYPE LOADED CORRECTLY!");
                        AddScripts(EnemyType);

                        Logger.LogInfo("Registering Sand Star as Enemy");
                        Levels.LevelTypes levelFlags = Levels.LevelTypes.All;
                        Enemies.SpawnType spawnType = Enemies.SpawnType.Outside;

                        TerminalNode sandstarNode = ScriptableObject.CreateInstance<TerminalNode>();
                        sandstarNode.displayText = "Sand Star \n\nDanger level: 15%\n\n" +
                            "It buries itself in the ground, and will try to keep in touch " +
                            "with its peers. Whenever threatened, it will jump out of the " +
                            "ground and fly towards player to pierce him through. It's not " +
                            "dangerous on its own, but a bigger group can be deadly!" +
                        "\n\n";

                        sandstarNode.clearPreviousText = true;
                        sandstarNode.maxCharactersToType = 2000;
                        sandstarNode.creatureName = "SandStar";
                        sandstarNode.creatureFileID = 963287;

                        TerminalKeyword sandstarKeyword = TerminalUtils.CreateTerminalKeyword("sandstar", specialKeywordResult: sandstarNode);
                        LethalLib.Modules.NetworkPrefabs.RegisterNetworkPrefab(EnemyType.enemyPrefab);
                        Enemies.RegisterEnemy(EnemyType, 30, levelFlags, spawnType, sandstarNode, sandstarKeyword);
                    }
                    
                }
                
            }
            else
            {
                Logger.LogInfo($"Plugin {"SandStar"} INVALID PATH!");
            }
            //NetworkPatches.RegisterPrefab(EnemyType.enemyPrefab);            
        }


        private void AddScripts(EnemyType startType)
        {             
            GameObject sandStarPrefab = startType.enemyPrefab;

            //Add enemyAI and fill fields
            sandStarPrefab.AddComponent<SandStarAI>();
            sandStarPrefab.GetComponent<SandStarAI>().enemyType = startType;
            sandStarPrefab.GetComponent<EnemyAICollisionDetect>().mainScript = sandStarPrefab.GetComponent<SandStarAI>();            

            //add ground sensor           
            if(sandStarPrefab.transform.Find("SandStar") == null)
            {
                Debug.Log("Sand star null");
            }
            else
            {
                if (sandStarPrefab.transform.Find("SandStar").Find("GroundSensor") == null)
                {
                    Debug.Log("GroundSensor null");
                }
            }
            sandStarPrefab.transform.Find("SandStar").Find("GroundSensor").gameObject.AddComponent<GroundSensor>();            
            sandStarPrefab.transform.Find("SandStar").Find("GroundSensor").gameObject.GetComponent<GroundSensor>().star = sandStarPrefab.GetComponent<SandStarAI>();
        }



        private void LoadNetWeaver()
        {            
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                Logger.LogInfo($"Plugin {"SandStar"} ASSEMBLY TYPE: "+type.Name);
                // ? prevents the compatibility layer from crashing the plugin loading
                try
                {
                    var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    foreach (var method in methods)
                    {
                        var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                        if (attributes.Length > 0)
                        {
                            method.Invoke(null, null);
                        }
                    }
                }
                catch
                {
                    //Log.LogWarning($"NetWeaver is skipping {type.FullName}");
                }
            }
        }
    }

    public static class Assets
    {
        // Replace mbundle with the Asset Bundle Name from your unity project 
        public static string mainAssetBundleName = "sandstarbundle";
        public static AssetBundle MainAssetBundle = null;

        private static string GetAssemblyName() => Assembly.GetExecutingAssembly().GetName().Name;
        public static void PopulateAssets()
        {
            if (MainAssetBundle == null)
            {                
                Console.WriteLine(GetAssemblyName() + "." + mainAssetBundleName);
                using (var assetStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(GetAssemblyName() + "." + mainAssetBundleName))
                {
                    MainAssetBundle = AssetBundle.LoadFromStream(assetStream);
                }

            }
        }
    }
}
