using BepInEx.Configuration;

namespace QOL.Items;

public class ItemManager
{
    public string Name { get; }
    public int GameStackSize { get; }

    public ConfigEntry<int> ItemMaxStackSize { get; }

    public ItemManager(ItemDrop item)
    {
        Name = GetItemName(item);
        GameStackSize = item.m_itemData.m_shared.m_maxStackSize;

        string itemName = Name.Length > 0 ? Name : item.m_itemData.m_shared.m_name;
        ItemMaxStackSize = QOL.Configs.Bind("6 - Item Tweaks", $"{itemName}_max_stack", 0, new ConfigDescription($"Any value above 0 will be used instead of vanilla stack size. Original Stack Size {GameStackSize}", new AcceptableValueRange<int>(0, 1000)));
        QOL._itemChanges.Add(itemName, ItemMaxStackSize);
    }

    public void SetStackSize(ItemDrop item)
    {
        int configValue = ItemMaxStackSize.Value;
        int value = configValue > 0 ? configValue : GameStackSize;
        if (value != GameStackSize && value > 0)
        {
            item.m_itemData.m_shared.m_maxStackSize = value;
            QOL.QOLLogger.LogInfo($"{QOL.LogPrefix} {Name} changing max stack size from: {GameStackSize} to {value}");
        };
    }

    public string GetItemName(ItemDrop item)
    {
        if (item.m_itemData.m_shared.m_name.StartsWith("$item_"))
            return item.m_itemData.m_shared.m_name.Substring(6, item.m_itemData.m_shared.m_name.Length - 6);

        return string.Empty;
    }
}