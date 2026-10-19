using UnityEngine;
using UnityEngine.Events;

public class Interactable_Object : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject interactPrompt;

    [Header("Unity Events")]
    [SerializeField] private UnityEvent OnInteract;
    [SerializeField] private UnityEvent OnEnter;
    [SerializeField] private UnityEvent OnExit;

    private void Start()
    {
        interactPrompt.SetActive(false);
    }

    public void Interact()
    {
        OnInteract.Invoke();
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
