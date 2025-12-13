using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShopRow : MonoBehaviour
{
    public Image Image;
    public GameObject[] TierIcons;
    public GameObject[] CostIcons;
    public TMP_Text Description;
    Action m_OnClick;
    
    public void Setup(Sprite image, int tier, int cost, string description, Action onClick)
    {
        Image.sprite = image;
        for (int i = 0; i < TierIcons.Length; i++)
            TierIcons[i].SetActive(i < tier);
        for (int i = 0; i < CostIcons.Length; i++)
            CostIcons[i].SetActive(i < cost);
        Description.text = description;
        m_OnClick = onClick;
    }
    
    public void OnClick() => m_OnClick?.Invoke();
    
}