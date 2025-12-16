using System.Linq;
using System.Text;
using Unity.Entities;
using UnityEngine;

public class RootLevelSystemGroupsTest : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Debug.Log($"Creating Game...");
        var worldSystemFilterFlags = WorldSystemFilterFlags.LocalSimulation;
        var m_World = new World("Probably crashes!", WorldFlags.Game);

        // ~~ Reduce systems ~~
        {
            var systems = DefaultWorldInitialization.GetAllSystems(worldSystemFilterFlags).ToList();

            //var filteredGroup = typeof(SurvivorSimulationSystemGroup);
            //var filteredGroupAssembly = filteredGroup.Assembly;
            //Debug.Log($"Filtering out {filteredGroup.FullName} assembly: {filteredGroupAssembly.FullName}");
            //systems.RemoveAll(s => s.GetCustomAttributes(typeof(UpdateInGroupAttribute), true).Any(u => ((UpdateInGroupAttribute)u).GroupType.Assembly == filteredGroupAssembly));

            StringBuilder sb = new();
            sb.AppendLine($"Remaining systems:");
            foreach (var remainingSystemGroups in systems.GroupBy(s => s.Assembly))
            {
                sb.AppendLine($"{remainingSystemGroups.Key.FullName}:");
                foreach (var pair in remainingSystemGroups)
                    sb.AppendLine($"\t{pair.FullName}:");
            }

            Debug.Log(sb.ToString());

            //var systemsIndicies = systems.Select(s => TypeManager.GetSystemTypeIndex(s)).ToHashSet();
            //foreach (var system in m_World.Systems)
            //{
            //    var systemTypeIndex = TypeManager.GetSystemTypeIndex(system.GetType());
            //    if (!systemsIndicies.Contains(systemTypeIndex))
            //    {
            //        system.Enabled = false;
            //    }
            //
            //}
            //var systemsUnmanaged = m_World.Unmanaged.GetAllSystems(Allocator.Temp);
            //foreach (var system in systemsUnmanaged)
            //{
            //    var systemTypeIndex = m_World.Unmanaged.GetSystemTypeIndex(system);
            //    if (!systemsIndicies.Contains(systemTypeIndex))
            //        m_World.Unmanaged.ResolveSystemStateRef(system).Enabled = false;
            //}

            Debug.Log($"Adding systems...");
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(m_World, systems);
            Debug.Log($"Added system to root level system groups");
        }
    }
}
