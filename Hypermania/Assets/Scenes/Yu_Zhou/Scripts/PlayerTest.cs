using Game.View;
using Game.View.Overlay;
using UnityEngine;

public class PlayerTest : MonoBehaviour
{
    public int maxHealth = 50;
    public int health = 50;
    public HealthBarView healthBar;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        healthBar.SetMaxHealth(maxHealth);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            health -= 10;
            healthBar.SetHealth(health);
        }
    }
}
