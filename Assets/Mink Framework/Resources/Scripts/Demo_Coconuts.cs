using UnityEngine;

public class Demo_Coconuts : MonoBehaviour
{
    [SerializeField] private int coconutsCollected;

    public void Collect(GameObject interactingObject)
    {
        coconutsCollected ++;
        Destroy(interactingObject);
    }

    public bool CheckCoconutsCompletion()
    {
        if (coconutsCollected >= 5)
        {
            coconutsCollected -= 5;
            return true;
        }
        else
        {
            return false;
        }
    }
}
