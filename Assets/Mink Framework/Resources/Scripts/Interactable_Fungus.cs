using UnityEngine;
using UnityEngine.Events;
using Fungus;

[RequireComponent(typeof(Character))]
[RequireComponent(typeof(Flowchart))]
public class Interactable_Fungus : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Flowchart flow;
    [SerializeField] private GameObject interactPrompt;

    [Header("Unity Events")]
    [SerializeField] private UnityEvent OnEnter;
    [SerializeField] private UnityEvent OnExit;

    private Player_Movement player;

    private bool flowchartRunning;

    private void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player").GetComponent<Player_Movement>();
        interactPrompt.SetActive(false);
    }

    private void Update()
    {
        if (flowchartRunning && !flow.HasExecutingBlocks())
        {
            player.StartCoroutine(player.EnableMovementWithDelay(0.3f));
            flowchartRunning = false;
        }
    }

    public void Interact()
    {
        interactPrompt.SetActive(false);
        flow.ExecuteBlock("Start");
        player.DisableMovement();
        flowchartRunning = true;
    }

    public void Enter()
    {
        interactPrompt.SetActive(true);
        OnEnter.Invoke();
    }

    public void Exit()
    {
        interactPrompt.SetActive(false);
        OnExit.Invoke();
    }
}
