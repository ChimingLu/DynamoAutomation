"""Generates the sample .dyn graphs (Dynamo 2.x JSON format, opens in Dynamo 2.3 - 4.x).

    python tools/make_samples.py <output folder>

The graphs only use core nodes, Revit's own nodes and DynamoAutomation zero-touch
functions, so no Python engine or third-party package is required.
"""
import json
import sys
import uuid
from pathlib import Path


def gid():
    return uuid.uuid4().hex


def port(name, description=""):
    return {"Id": gid(), "Name": name, "Description": description, "UsingDefaultValue": False,
            "Level": 2, "UseLevels": False, "KeepListStructure": False}


class Graph:
    def __init__(self, name, description):
        self.name, self.description = name, description
        self.nodes, self.views, self.connectors, self.annotations = [], [], [], []

    def _add(self, node, title, x, y, is_input=False, is_output=False):
        self.nodes.append(node)
        self.views.append({"ShowGeometry": True, "Name": title, "Id": node["Id"], "IsSetAsInput": is_input,
                           "IsSetAsOutput": is_output, "Excluded": False, "X": x, "Y": y})
        return node

    def string(self, title, value, x, y, is_input=True):
        return self._add({"ConcreteType": "CoreNodeModels.Input.StringInput, CoreNodeModels",
                          "NodeType": "StringInputNode", "InputValue": value, "Id": gid(), "Inputs": [],
                          "Outputs": [port("", "String")], "Replication": "Disabled",
                          "Description": "Creates a string."}, title, x, y, is_input)

    def boolean(self, title, value, x, y, is_input=True):
        return self._add({"ConcreteType": "CoreNodeModels.Input.BoolSelector, CoreNodeModels",
                          "NodeType": "BooleanInputNode", "InputValue": value, "Id": gid(), "Inputs": [],
                          "Outputs": [port("", "Boolean")], "Replication": "Disabled",
                          "Description": "Selection between a true and false."}, title, x, y, is_input)

    def code(self, title, code, inputs, outputs, x, y):
        return self._add({"ConcreteType": "Dynamo.Graph.Nodes.CodeBlockNodeModel, DynamoCore",
                          "NodeType": "CodeBlockNode", "Code": code, "Id": gid(),
                          "Inputs": [port(n, n) for n in inputs],
                          "Outputs": [port("", f"Value of expression at line {i + 1}") for i in range(outputs)],
                          "Replication": "Disabled",
                          "Description": "Allows for DesignScript code to be authored directly"}, title, x, y)

    def number(self, title, value, x, y, is_input=True):
        return self._add({"ConcreteType": "CoreNodeModels.Input.DoubleInput, CoreNodeModels",
                          "NumberType": "Double", "NodeType": "NumberInputNode", "InputValue": float(value),
                          "Id": gid(), "Inputs": [], "Outputs": [port("", "Double")], "Replication": "Disabled",
                          "Description": "Creates a number."}, title, x, y, is_input)

    def create_list(self, count, x, y):
        return self._add({"ConcreteType": "CoreNodeModels.CreateList, CoreNodeModels", "VariableInputPorts": True,
                          "NodeType": "ExtensionNode", "Id": gid(),
                          "Inputs": [port(f"item{i}", f"Item Index #{i}") for i in range(count)],
                          "Outputs": [port("list", "A list (type: var[]..[])")], "Replication": "Disabled",
                          "Description": "Makes a new list from the given inputs"}, "List Create", x, y)

    def function(self, title, signature, inputs, outputs, x, y, defaults=()):
        """defaults: input names left unconnected that should use the method's default value."""
        ins = [port(n) for n in inputs]
        for p in ins:
            p["UsingDefaultValue"] = p["Name"] in defaults
        return self._add({"ConcreteType": "Dynamo.Graph.Nodes.ZeroTouch.DSFunction, DynamoCore",
                          "NodeType": "FunctionNode", "FunctionSignature": signature, "Id": gid(),
                          "Inputs": ins, "Outputs": [port(n) for n in outputs],
                          "Replication": "Auto", "Description": title}, title, x, y)

    def vararg(self, title, signature, inputs, output, x, y):
        """Nodes with +/- input buttons, e.g. String.Concat, FileSystem.CombinePath, String.Split."""
        return self._add({"ConcreteType": "Dynamo.Graph.Nodes.ZeroTouch.DSVarArgFunction, DynamoCore",
                          "FunctionSignature": signature, "FunctionType": "VariableArgument",
                          "NodeType": "FunctionNode", "Id": gid(), "Inputs": [port(n) for n in inputs],
                          "Outputs": [port(output)], "Replication": "Auto", "Description": title}, title, x, y)

    def elements_of_category(self, x, y):
        return self._add({"ConcreteType": "DSRevitNodesUI.ElementsOfCategory, DSRevitNodesUI",
                          "NodeType": "ExtensionNode", "Id": gid(),
                          "Inputs": [port("Category", "The Category")],
                          "Outputs": [port("Elements", "An element type.")], "Replication": "Disabled",
                          "Description": "Get all elements of the specified category from the model."},
                         "All Elements of Category", x, y)

    def watch(self, x, y):
        return self._add({"ConcreteType": "CoreNodeModels.Watch, CoreNodeModels", "NodeType": "ExtensionNode",
                          "Id": gid(), "Inputs": [port("", "Node to evaluate.")],
                          "Outputs": [port("", "Watch contents.")], "Replication": "Disabled",
                          "Description": "Visualize the output of node."}, "Watch", x, y)

    def connect(self, a, a_port, b, b_port):
        self.connectors.append({"Start": a["Outputs"][a_port]["Id"], "End": b["Inputs"][b_port]["Id"], "Id": gid()})

    def note(self, title, nodes, color):
        self.annotations.append({"Id": gid(), "Title": title, "Nodes": [n["Id"] for n in nodes],
                                 "Left": 0, "Top": 0, "Width": 0, "Height": 0, "FontSize": 30.0,
                                 "InitialTop": 0, "InitialHeight": 0, "TextblockHeight": 0, "Background": color})

    def save(self, path):
        data = {
            "Uuid": str(uuid.uuid4()), "IsCustomNode": False, "Description": self.description, "Name": self.name,
            "ElementResolver": {"ResolutionMap": {}}, "Inputs": [], "Outputs": [],
            "Nodes": self.nodes, "Connectors": self.connectors,
            "Dependencies": [], "NodeLibraryDependencies": [], "Bindings": [],
            "View": {"Dynamo": {"ScaleFactor": 1.0, "HasRunWithoutCrash": True, "IsVisibleInDynamoLibrary": True,
                                "Version": "2.3.0.5885", "RunType": "Manual", "RunPeriod": "1000"},
                     "Camera": {"Name": "Background Preview", "EyeX": -17.0, "EyeY": 24.0, "EyeZ": 50.0,
                                "LookX": 12.0, "LookY": -13.0, "LookZ": -58.0, "UpX": 0.0, "UpY": 1.0, "UpZ": 0.0},
                     "NodeViews": self.views, "Annotations": self.annotations,
                     "X": 40.0, "Y": 120.0, "Zoom": 0.8},
        }
        Path(path).write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def worker(path):
    g = Graph("CountWalls_Worker", "Runs inside each Revit model of a batch: counts walls, appends one CSV row, then Job.Finish closes Revit.")
    cat_name = g.string("Category (BuiltInCategory)", "OST_Walls", 0, 0, is_input=False)
    cat = g.function("Category.ByName", "Revit.Elements.Category.ByName@string", ["name"], ["Category"], 300, 0)
    elems = g.elements_of_category(560, 0)
    count = g.function("List.Count", "DSCore.List.Count@var[]..[]", ["list"], ["int"], 860, 0)

    # one CSV row per model: <output folder>\wall_count.csv
    doc = g.function("Document.Current", "Revit.Application.Document.Current", [], ["Document"], 0, 250)
    file_path = g.function("Document.FilePath", "Revit.Application.Document.FilePath", ["document"], ["string"],
                           300, 250)
    model = g.function("Job.ModelPath", "DynamoAutomation.Job.ModelPath@string", ["manualRunPath"], ["string"],
                       560, 250)
    out_dir = g.function("Job.OutputDirectory", "DynamoAutomation.Job.OutputDirectory", [], ["string"], 560, 450)
    csv_name = g.string("CSV 檔名", "wall_count.csv", 560, 600, is_input=False)
    csv = g.vararg("FileSystem.CombinePath", "DSCore.IO.FileSystem.CombinePath@string[]", ["string0", "string1"],
                   "string", 860, 450)
    values = g.create_list(2, 1160, 150)
    header_text = g.string("欄名（逗號分隔）", "model,walls", 860, 700, is_input=False)
    comma = g.string("逗號", ",", 860, 850, is_input=False)
    header = g.vararg("String.Split", "DSCore.String.Split@string,string[]", ["str", "separater0"], "strings",
                      1160, 700)
    row = g.function("Report.AppendCsvRow", "DynamoAutomation.Report.AppendCsvRow@string,var[],string[]",
                     ["filePath", "values", "header"], ["string"], 1460, 400)
    # Job.Finish must run last: it waits for the report row, then saves (per the batch's
    # saveMode) and closes Revit. When the graph is run by hand it does nothing.
    finish = g.function("Job.Finish（一定放最後）", "DynamoAutomation.Job.Finish@var[]..[],string", ["waitFor", "message"],
                        ["string"], 1800, 400, defaults=("message",))
    out = g.watch(2150, 400)
    g.connect(cat_name, 0, cat, 0)
    g.connect(cat, 0, elems, 0)
    g.connect(elems, 0, count, 0)
    g.connect(doc, 0, file_path, 0)
    g.connect(file_path, 0, model, 0)
    g.connect(out_dir, 0, csv, 0)
    g.connect(csv_name, 0, csv, 1)
    g.connect(model, 0, values, 0)
    g.connect(count, 0, values, 1)
    g.connect(header_text, 0, header, 0)
    g.connect(comma, 0, header, 1)
    g.connect(csv, 0, row, 0)
    g.connect(values, 0, row, 1)
    g.connect(header, 0, row, 2)
    g.connect(row, 0, finish, 0)
    g.connect(finish, 0, out, 0)
    g.note("① 數牆", [cat_name, cat, elems, count], "#FFB9F9E1")
    g.note("② 每個模型寫一列到 wall_count.csv", [doc, file_path, model, out_dir, csv_name, csv, values, header_text,
                                              comma, header, row], "#FFA4E1FF")
    g.note("③ Job.Finish：存檔並關閉 Revit（只在批次時動作）", [finish, out], "#FFFFAA45")
    g.save(path)


FIND_MODELS = "DynamoAutomation.Batch.FindModels@string,bool,bool"
RUN_GRAPH = ("DynamoAutomation.Batch.RunGraph@string[],string,string,var,string,bool,int,int,"
             "string[],string[],bool")
VALUE_AT_KEY = "DesignScript.Builtin.Dictionary.ValueAtKey@string"
RUN_GRAPH_INPUTS = ["modelPaths", "workspacePath", "outputDirectory", "revitVersion", "saveMode", "openDetached",
                    "maxParallel", "timeoutMinutes", "parameterKeys", "parameterValues", "run"]


def build_master(path, name, description, worker_path, output_dir, revit_version, parallel, parameters=None):
    """Master graph made only of ordinary nodes, so every wire into Batch.RunGraph is visible.

    parameters: optional [(key, label, default value)] passed to the worker graph via Job.Parameter.
    """
    g = Graph(name, description)
    y = 0

    def row(step=150):
        nonlocal y
        y += step
        return y - step

    # ① inputs (Is Input: also usable from Dynamo Player)
    # path strings wrap over several lines, so they get taller rows
    folder = g.string("模型資料夾", r"C:\DynamoAutomation\models", 0, row(200))
    subfolders = g.boolean("包含子資料夾", False, 0, row())
    graph = g.string("工作圖 (.dyn)", worker_path, 0, row(300))
    output = g.string("輸出資料夾", output_dir, 0, row(200))
    version = g.number("Revit 版本", revit_version, 0, row())
    save = g.string("存檔模式 (none / copy / overwrite)", "none", 0, row())
    detached = g.boolean("分離開啟協同模型", False, 0, row())
    maxp = g.number("同時執行的 Revit 數", parallel, 0, row())
    timeout = g.number("逾時（分鐘）", 60, 0, row())
    param_inputs = [g.string(label, value, 0, row()) for _, label, value in (parameters or [])]
    run = g.boolean("執行", False, 0, row())
    inputs = [folder, subfolders, graph, output, version, save, detached, maxp, timeout] + param_inputs + [run]

    # ② models
    find = g.function("Batch.FindModels", FIND_MODELS, ["directory", "includeSubfolders", "includeFamilies"],
                      ["string[]"], 520, 0, defaults=("includeFamilies",))
    g.connect(folder, 0, find, 0)
    g.connect(subfolders, 0, find, 1)

    # ③ parameters for the worker graph: two lists, keys and values
    extra = []
    if parameters:
        keys = [g.string(f"參數名稱 {i + 1}", key, 520, 1250 + i * 150, is_input=False)
                for i, (key, _, _) in enumerate(parameters)]
        key_list = g.create_list(len(keys), 860, 1250)
        value_list = g.create_list(len(param_inputs), 860, 1550)
        for i, k in enumerate(keys):
            g.connect(k, 0, key_list, i)
        for i, v in enumerate(param_inputs):
            g.connect(v, 0, value_list, i)
        extra = keys + [key_list, value_list]

    # ④ batch
    defaults = () if parameters else ("parameterKeys", "parameterValues")
    batch = g.function("Batch.RunGraph", RUN_GRAPH, RUN_GRAPH_INPUTS, ["var"], 1200, 500, defaults=defaults)
    wiring = {"modelPaths": find, "workspacePath": graph, "outputDirectory": output, "revitVersion": version,
              "saveMode": save, "openDetached": detached, "maxParallel": maxp, "timeoutMinutes": timeout, "run": run}
    if parameters:
        wiring.update(parameterKeys=key_list, parameterValues=value_list)
    for i, name_ in enumerate(RUN_GRAPH_INPUTS):
        if name_ in wiring:
            g.connect(wiring[name_], 0, batch, i)

    # ⑤ results: the node returns a dictionary; read single values with Dictionary.ValueAtKey
    status_key = g.string("鍵：status", "status", 1700, 760, is_input=False)
    summary_key = g.string("鍵：summaryFile", "summaryFile", 1700, 1060, is_input=False)
    status = g.function("Dictionary.ValueAtKey", VALUE_AT_KEY, ["dictionary", "key"], ["value"], 2000, 650)
    summary = g.function("Dictionary.ValueAtKey", VALUE_AT_KEY, ["dictionary", "key"], ["value"], 2000, 950)
    for node, key in ((status, status_key), (summary, summary_key)):
        g.connect(batch, 0, node, 0)
        g.connect(key, 0, node, 1)
    w_all = g.watch(2000, 250)
    w_status = g.watch(2350, 650)
    w_summary = g.watch(2350, 950)
    g.connect(batch, 0, w_all, 0)
    g.connect(status, 0, w_status, 0)
    g.connect(summary, 0, w_summary, 0)

    g.note("① 輸入（Is Input，也可用 Dynamo Player 執行）", inputs, "#FFFFC999")
    g.note("② 找模型（自動略過 *.0001.rvt 備份）", [find], "#FFB9F9E1")
    if parameters:
        g.note("③ 傳給工作圖的參數（工作圖用 Job.Parameter 讀取）", extra, "#FFD8D8D8")
    g.note("④ 批次：每個模型一個 Revit（執行=false 時只檢查）", [batch], "#FFC1D676")
    g.note("⑤ 讀取結果：每個模型的狀態與總表路徑", [status_key, summary_key, status, summary, w_all, w_status, w_summary],
           "#FFA4E1FF")
    g.save(path)


def master(path):
    build_master(path, "RunBatch_Master",
                 "Runs a worker graph on every Revit model in a folder (Dynamo Sandbox, DynamoCLI or Dynamo for Revit).",
                 r"C:\DynamoAutomation\samples\CountWalls_Worker.dyn", r"C:\DynamoAutomation\output", 2024, 1)



if __name__ == "__main__":
    target = Path(sys.argv[1] if len(sys.argv) > 1 else "samples")
    target.mkdir(parents=True, exist_ok=True)
    worker(target / "CountWalls_Worker.dyn")
    master(target / "RunBatch_Master.dyn")
    print("written to", target.resolve())
