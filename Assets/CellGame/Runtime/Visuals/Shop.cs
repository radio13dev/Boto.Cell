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
    EntityQuery m_DnaChangeQuery;
    public ShopRow DrillTierRow;
    

    private void Awake()
    {
        Instance = this;
        ClosedTransform.Apply((RectTransform)transform);
        m_ShopQuery = World.DefaultGameObjectInjectionWorld.EntityManager.CreateEntityQuery(typeof(ShopData));
        m_DnaQuery = World.DefaultGameObjectInjectionWorld.EntityManager.CreateEntityQuery(typeof(DNA));
        m_DnaChangeQuery = World.DefaultGameObjectInjectionWorld.EntityManager.CreateEntityQuery(typeof(DNA.Changes));
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
            ulong totalCost = (ulong)2<<drillCost;
            
            var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            var dna = m_DnaQuery.GetSingleton<DNA>();
            Debug.Log($"Attemptign to pay {totalCost} of {dna.Value}");
            if (totalCost > dna.Value) return;
            
            var changes = m_DnaChangeQuery.GetSingleton<DNA.Changes>();
            changes.Data.Add(new DNA.Change(default, -(long)totalCost));
            m_DnaChangeQuery.SetSingleton(changes);
            
            dna.Subtract(totalCost);
            m_DnaQuery.SetSingleton(dna);
            
            shopData.DrillTier++;
            m_ShopQuery.SetSingleton(shopData);
            
            // Refresh should be triggered by money update
            // Refresh();
        });
    }
}