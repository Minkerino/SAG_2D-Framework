using UnityEngine;
using Fungus;

[RequireComponent(typeof(Character))]
[RequireComponent(typeof(Flowchart))]
public class Interactable_Fungus : MonoBehaviour
{
    [SerializeField] private Flowchart flow;
    [SerializeField] private GameObject interactPrompt;

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

    public void ShowInteractPrompt()
    {
        interactPrompt.SetActive(true);
    }

    public void HideInteractPrompt()
    {
        interactPrompt.SetActive(false);
    }

    public void Interact()
    {
        HideInteractPrompt();
        flow.ExecuteBlock("Start");
        player.DisableMovement();
        flowchartRunning = true;
    }
}
