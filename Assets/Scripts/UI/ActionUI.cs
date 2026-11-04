using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;

public class ActionUI : MonoBehaviour
{
    [Header("引用")]

    public Transform player1;
    public Transform player2;
    public Transform canvasP1;
    public Transform canvasP2;
    public MoveLate moveLate;
    public TextMeshProUGUI[] queueTexts; // P1的6个TMP（顺序从左到右）
    public TextMeshProUGUI currentActionText; // P2的TMP
    public float updateInterval = 0.1f; // 每0.1秒更新一次UI

    private float lastUpdateTime;

    void Start()
    {
        if (moveLate == null)
            moveLate = FindObjectOfType<MoveLate>();
        lastUpdateTime = -updateInterval;

        if (moveLate == null)
            moveLate = GetComponentInChildren<MoveLate>();
        if (currentActionText == null)
            currentActionText = GetComponentInChildren<TextMeshProUGUI>();
    }

    void Update()
    {
        canvasP1.position = player1.position + new Vector3(0, 1f, 0);
        canvasP2.position = player2.position + new Vector3(0, 1f, 0);

        if (Time.time - lastUpdateTime >= updateInterval)
        {
            lastUpdateTime = Time.time;
            RefreshUI();
        }
        
        if (moveLate == null) return;
        MoveLate.ActionType act = moveLate.GetCurrentAction();
        currentActionText.text = ActionToSymbol(act);
    }

    void RefreshUI()
    {
        if (moveLate == null) return;
        List<MoveLate.ActionType> actions = moveLate.GetPendingActions();
        for (int i = 0; i < queueTexts.Length; i++)
        {
            if (i < actions.Count)
                queueTexts[i].text = ActionToSymbol(actions[i]);
            else
                queueTexts[i].text = "";
        }
    }

    string ActionToSymbol(MoveLate.ActionType act)
    {
        switch (act)
        {
            case MoveLate.ActionType.MoveLeft:   return "←";
            case MoveLate.ActionType.MoveRight:  return "→";
            case MoveLate.ActionType.Jump:       return "↑";
            case MoveLate.ActionType.DashLeft:   return "《";
            case MoveLate.ActionType.DashRight:  return "》";
            case MoveLate.ActionType.ClimbUp:    return "↑";
            case MoveLate.ActionType.ClimbDown:  return "↓";
            default: return "";
        }
    }
}