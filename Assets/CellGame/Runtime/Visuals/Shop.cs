using System;
using System.Collections;
using System.Linq;
using Drawing;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

public class Shop : MonoBehaviour
{
    public static Shop Instance;
    public const float AnimationTransitionTime = 0.2f;

    bool m_IsOpen;
    ExclusiveCoroutine co;
    
    public ease.Mode Easing = ease.Mode.cubic_out;
    public TransitionPoint ClosedTransform;
    public TransitionPoint OpenTransform;
    
    EntityQuery m_ShopQuery;
    EntityQuery m_DnaQuery;
    public ShopRow DrillTierRow;
    

    private void Awake()
    {
        Instance = this;
        ClosedTransform.Apply((RectTransform)transform);
        m_ShopQuery = World.DefaultGameObjectInjectionWorld.EntityManager.CreateEntityQuery(typeof(ShopData));
        m_DnaQuery = World.DefaultGameObjectInjectionWorld.EntityManager.CreateEntityQuery(typeof(DNA), typeof(DNA.Group), typeof(DNA.Particle), typeof(DNA.Merger));
    }

    public void Toggle()
    {
        if (m_IsOpen)
        {
            co.StartCoroutine(this, ClosedTransform.Lerp((RectTransform)transform, AnimationTransitionTime, Easing));
        }
        else
        {
            Refresh();
            co.StartCoroutine(this, OpenTransform.Lerp((RectTransform)transform, AnimationTransitionTime, Easing));
        }
        m_IsOpen = !m_IsOpen;
    }
    
    public void Refresh()
    {
        // Load the shop purchases
        var shopData = m_ShopQuery.GetSingleton<ShopData>();
        
        int drillCost = 1 + shopData.DrillTier;
        DrillTierRow.Setup(default, shopData.DrillTier, drillCost,  "Drill tier", () =>
        {
            var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            // Consume the cost from any sources
            using var dnas = m_DnaQuery.ToComponentDataArray<DNA>(Allocator.Temp);
            using var entities = m_DnaQuery.ToEntityArray(Allocator.Temp);
            
            ulong leftToDrain = (ulong)4 << (drillCost-1);
            ulong has = 0;
            for (int i = 0; i < dnas.Length; i++)
            {
                has += dnas[i].Value;
                if (has >= leftToDrain) break;
            }
            if (has < leftToDrain) return;
            
            shopData.DrillTier++;
            m_ShopQuery.SetSingleton(shopData);
            
            for (int i = 0; i < dnas.Length; i++)
            {
                if (dnas[i].Value <= 0) continue;
                
                var taken = math.min(dnas[i].Value, leftToDrain);
                if (taken == 0) continue;
                
                
                var left = dnas[i].Value - taken;
                entityManager.SetComponentData<DNA>(entities[i], new DNA(){ Value = left });
                
                // Fix up visuals
                var groups = entityManager.GetBuffer<DNA.Group>(entities[i]);
                var particles = entityManager.GetBuffer<DNA.Particle>(entities[i]);
                var mergers = entityManager.GetBuffer<DNA.Merger>(entities[i]);
                
                // Complete all mergers
                while (mergers.Length > 0)
                {
                    Virus_InputSystem.Job.CompleteMerger(ref mergers, ref particles, ref mergers.ElementAt(0), default, default);
                    mergers.RemoveAt(0);
                }
                
                // Reduce group count
                var availableGroups = math.max((1 + 64 - math.lzcnt(left)) / 2, 1);
                while (groups.Length > availableGroups)
                    groups.RemoveAt(groups.Length - 1);
                
                // Only show particles and groups that are left
                for (int groupIndex = 0; (groupIndex << 2) < particles.Length; groupIndex++)
                {
                    for (int offset = 0; offset < 3; offset++)
                    {
                        particles.ElementAt(offset + (groupIndex << 2)).Active = ((left >> (groupIndex*2)) & 3) > (uint)offset;
                    }
                }
            }
            
            Refresh();
            
        });
    }
}