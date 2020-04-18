// Inventory management
// glen@glenmurphy.com
//
// Displays your Ores, Ingots, Components, and Production Queue, and auto-queues component 
// production so you always have a specified minimum quantity of components
//
// - Describe your minimum quantities in the definition of MinimumComponents
// - Set assembler name in Main();
// - Set panel names in Main();

// Global variables
private static Dictionary<string, float> StoredIngots = new Dictionary<string, float> {
  { "Cobalt", 0 },
  { "Gold", 0 },
  { "Iron", 0 },
  { "Magnesium", 0 },
  { "Nickel", 0 },
  { "Platinum", 0 },
  { "Silicon", 0 },
  { "Silver", 0 },
  { "Stone", 0 },
  { "Uranium", 0 }
};
private static Dictionary<string, float> StoredOres = new Dictionary<string, float>(StoredIngots);
private static Dictionary<string, float> StoredComponents = new Dictionary<string, float>();
private static Dictionary<string, float> QueuedComponents = new Dictionary<string, float>();

// From https://3dpeg.net/archives/425
// Item names are sometimes different from the blueprint names, this maps them
private static Dictionary<string, string> MapComponentToBlueprint = new Dictionary<string, string> {
  { "SteelPlate", "SteelPlate" },
  { "Construction", "ConstructionComponent" },
  { "PowerCell", "PowerCell" },
  { "Computer", "ComputerComponent" },
  { "LargeTube", "LargeTube" },
  { "Motor", "MotorComponent" },
  { "Display", "Display" },
  { "MetalGrid", "MetalGrid" },
  { "InteriorPlate", "InteriorPlate" },
  { "SmallTube", "SmallTube" },
  { "RadioCommunication", "RadioCommunicationComponent" },
  { "BulletproofGlass", "BulletproofGlass" },
  { "Girder", "GirderComponent" },
  { "Explosives", "ExplosivesComponent" },
  { "Detector", "DetectorComponent" },
  { "Medical", "MedicalComponent" },
  { "GravityGenerator", "GravityGeneratorComponent" },
  { "Superconductor", "Superconductor" },
  { "Thrust", "ThrustComponent" },
  { "Reactor", "ReactorComponent" },
  { "SolarCell", "SolarCell" },
  { "Canvas", "Canvas" }
};
private static Dictionary<string, string> MapBlueprintToComponent = new Dictionary<string, string>();

// Use the component names, not the blueprint names
private static Dictionary<string, float> MinimumComponents = new Dictionary<string, float> {
  { "SteelPlate", 2000 },
  { "InteriorPlate", 2000 },
  { "Construction", 1000 },
  { "MetalGrid", 1000 },
  { "SmallTube", 1000 },
  { "LargeTube", 200 },
  { "Motor", 1000 },
  { "Girder", 1000 },
  { "BulletproofGlass", 500 },
  { "Display", 500 },
  { "Computer", 500 },
  { "Reactor", 500 },
  { "Superconductor", 500 },
  { "Thrust", 200 },
  { "GravityGenerator", 200 },
  { "Medical", 500 },
  { "RadioCommunication", 500 },
  { "Detector", 500 },
  { "Explosives", 500 },
  { "SolarCell", 1000 },
  { "PowerCell", 1000 },
  { "Canvas", 100 }
};
private static Boolean inited = false;

// Random variables to avoid in-method allocations (I don't really understand C#'s
// approach here, but OK)
private List<IMyAssembler> assemblers = new List<IMyAssembler>();
private List<String> output = new List<String>();
private List<MyInventoryItem> inventoryItems = new List<MyInventoryItem>();
private List<MyProductionItem> productionItems = new List<MyProductionItem>();
private List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();

// Seconds; set highish so we don't kill Dan's server
// but this seems broken - GetTimeDelta seems to report
// 1/3 realtime, which makes no sense
// also note UpdateFrequency
private float tickInterval = 5;
private System.DateTime lastTime;
private float timeAccumulator;

public Program() {
  Runtime.UpdateFrequency = UpdateFrequency.Update100;
}

void IndexItem(Dictionary<string, float> type, String subtype, float amount) {
  if (type.ContainsKey(subtype))
    type[subtype] += amount;
  else
    type.Add(subtype, amount);
}

void IndexInventory(IMyInventory inv) {
  inventoryItems.Clear();
  inv.GetItems(inventoryItems);

  for (int i = 0; i < inventoryItems.Count; i++) {
    MyInventoryItem item = inventoryItems[i];
    if (item.Amount <= 0) continue;
    if (item.Type.TypeId.ToString().Equals("MyObjectBuilder_Ingot"))
      IndexItem(StoredIngots, item.Type.SubtypeId, (float)item.Amount);
    else if (item.Type.TypeId.ToString().Equals("MyObjectBuilder_Ore"))
      IndexItem(StoredOres, item.Type.SubtypeId, (float)item.Amount);
    else if (item.Type.TypeId.ToString().Equals("MyObjectBuilder_Component"))
      IndexItem(StoredComponents, item.Type.SubtypeId, (float)item.Amount);
  }
}

void IndexQueue(IMyProductionBlock prod) {
  productionItems.Clear();
  prod.GetQueue(productionItems);

  for (int i = 0; i < productionItems.Count; i++) {
    MyProductionItem item = productionItems[i];
    if (item.BlueprintId.TypeId.ToString().Equals("MyObjectBuilder_BlueprintDefinition")) {
      IndexItem(QueuedComponents, MapBlueprintToComponent[item.BlueprintId.SubtypeName], (float)item.Amount);
    }
  }
}

void IndexBlocks() {
  blocks.Clear();
  GridTerminalSystem.GetBlocks(blocks);
  for (int i = 0; i < blocks.Count; i++) {
    if (!blocks[i].HasInventory) continue;

    for (int j = 0; j < blocks[i].InventoryCount; j++) {
      IndexInventory(blocks[i].GetInventory(j));
    }
  }

  assemblers.Clear();
  GridTerminalSystem.GetBlocksOfType<IMyAssembler>(assemblers);
  foreach(IMyAssembler assembler in assemblers) {
    IndexQueue(assembler);
  }
}

bool ProcessMinimums(String assemblerName) {
  bool itemsAdded = false;
  IMyAssembler assembler = GridTerminalSystem.GetBlockWithName(assemblerName) as IMyAssembler;
  if (assembler == null) {
    Echo("Error: Assembler '" + assemblerName + "' not found");
    return false;
  }

  var enumerator = MinimumComponents.GetEnumerator();
  while (enumerator.MoveNext()) {
    float total = GetQueued(enumerator.Current.Key) + GetStored(enumerator.Current.Key);
    float missing = enumerator.Current.Value - total;
    if (missing <= 0)
      continue;
    MyDefinitionId blueprint = MyDefinitionId.Parse("MyObjectBuilder_BlueprintDefinition/" + 
                                                    MapComponentToBlueprint[enumerator.Current.Key]);
    assembler.AddQueueItem(blueprint, missing);

    // This is a bit of pre-caching so we don't need to reindex the whole lot; might cause sync
    // issues if something blocks adding things to the queue (not sure if that can happen)
    IndexItem(QueuedComponents, enumerator.Current.Key, missing); 
    itemsAdded = true;
  }

  return itemsAdded;
}

// Pretty sure I have some C# knowledge to gain because these seems daft
float GetStored(String index) {
  return (StoredComponents.ContainsKey(index)) ? StoredComponents[index] : 0F;
}

float GetQueued(String index) {
  return (QueuedComponents.ContainsKey(index)) ? QueuedComponents[index] : 0F;
}

public void InitDictionaries() {
  if (!inited) {
    StoredOres.Add("Scrap", 0);
    StoredOres.Add("Ice", 0);

    var enumerator = MapComponentToBlueprint.GetEnumerator();
    while (enumerator.MoveNext()) {
      StoredComponents.Add(enumerator.Current.Key, 0);
      QueuedComponents.Add(enumerator.Current.Key, 0);
      MapBlueprintToComponent.Add(enumerator.Current.Value, enumerator.Current.Key);
    }

    inited = true;
  } else {
    foreach(var key in StoredIngots.Keys.ToList()) {
      StoredIngots[key] = 0;
    }

    foreach(var key in StoredOres.Keys.ToList()) {
      StoredOres[key] = 0;
    }

    foreach(var key in StoredComponents.Keys.ToList()) {
      StoredComponents[key] = 0;
    }

    foreach(var key in QueuedComponents.Keys.ToList()) {
      QueuedComponents[key] = 0;
    }
  }
}

void DisplayOnPanel(String panelName, String text) {
  IMyTextPanel lcd = GridTerminalSystem.GetBlockWithName(panelName) as IMyTextPanel;
  if (lcd == null) {
    Echo("Error: Panel '"+panelName+"' not found");
    return;
  }

  lcd.ContentType = ContentType.TEXT_AND_IMAGE;
  lcd.Font = "Monospace";
  int length = text.Split('\n').Length;
  lcd.FontSize = (length > 13) ? (1.3F * 13 / length) : 1.3F;
  lcd.WriteText(text, false);
}

void DisplayStored(String panelName, String title, Dictionary<string, float> data ) {
  output.Clear();
  var enumerator = data.GetEnumerator();
  while (enumerator.MoveNext()) { 
    output.Add(enumerator.Current.Key.PadRight(12, ' ') + 
               Math.Round(enumerator.Current.Value).ToString().PadLeft(7));
  }
  output.Sort();
  output.Insert(0, title);
  String text = String.Join("\n", output.ToArray());

  DisplayOnPanel(panelName, text);
}

void DisplayComponents(String panelName) {
  output.Clear();
  var enumerator = StoredComponents.GetEnumerator();
  while (enumerator.MoveNext()) {
    output.Add(enumerator.Current.Key.PadRight(20, ' ') +
               Math.Round(enumerator.Current.Value).ToString().PadLeft(7) +
               Math.Round(GetQueued(enumerator.Current.Key)).ToString().PadLeft(7));
  }
  output.Sort();
  output.Insert(0, "Components           Stored  Queue");
  String text = String.Join("\n", output.ToArray());

  DisplayOnPanel(panelName, text);
}

public float getTimeDelta() {
  System.DateTime now = System.DateTime.UtcNow;

  if (lastTime == System.DateTime.MinValue) {
    lastTime = now;
    return tickInterval;
  }

  float dt = (float)(now - lastTime).Milliseconds / 1000f;
  lastTime = now;
  return dt;
}

public void Main(string argument, UpdateType updateSource) {
  float dt = getTimeDelta();

  timeAccumulator += dt;
  if (timeAccumulator < tickInterval)
    return;
  
  timeAccumulator -= tickInterval;

  // Indexing what we have
  InitDictionaries();
  IndexBlocks();

  // Auto-queue new components
  ProcessMinimums("B1: Master Assembler");

  // Output
  DisplayStored("L1: LCD Panel 3", "Oreses", StoredOres);
  DisplayStored("L1: LCD Panel 2", "Ingots", StoredIngots);
  DisplayComponents("L1: LCD Panel 1");
}