using UnityEngine;

// Points de vie et inventaire (5 cases) du joueur local.
public class PlayerStatus : MonoBehaviour
{
    public int Hp { get; private set; } = GameRules.MaxHp;
    public bool Dead { get; private set; }
    public int Selected { get; private set; } = -1;

    readonly ItemType[] slots = new ItemType[GameRules.InventorySize];

    public event System.Action Changed;

    public ItemType this[int slot] => slots[slot];

    public int ItemCount
    {
        get
        {
            int n = 0;
            foreach (var s in slots) if (s != ItemType.None) n++;
            return n;
        }
    }

    public int FreeSlots => GameRules.InventorySize - ItemCount;

    public int Add(ItemType type)
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] == ItemType.None)
            {
                slots[i] = type;
                Changed?.Invoke();
                return i;
            }
        return -1;
    }

    public ItemType RemoveAt(int slot)
    {
        ItemType t = slots[slot];
        slots[slot] = ItemType.None;
        if (Selected == slot) Selected = -1;
        Changed?.Invoke();
        return t;
    }

    public void ClearInventory()
    {
        for (int i = 0; i < slots.Length; i++) slots[i] = ItemType.None;
        Selected = -1;
        Changed?.Invoke();
    }

    // Touche 1 à 5 : sélectionne la case, ou la désélectionne si elle l'était déjà.
    public void ToggleSelect(int slot)
    {
        Selected = Selected == slot ? -1 : slot;
        Changed?.Invoke();
    }

    public void Deselect()
    {
        if (Selected < 0) return;
        Selected = -1;
        Changed?.Invoke();
    }

    public void SetHp(int hp)
    {
        hp = Mathf.Clamp(hp, 0, GameRules.MaxHp);
        if (hp == Hp) return;
        Hp = hp;
        Changed?.Invoke();
    }

    public void SetDead(bool dead)
    {
        Dead = dead;
        if (dead) Hp = 0;
        Changed?.Invoke();
    }
}
