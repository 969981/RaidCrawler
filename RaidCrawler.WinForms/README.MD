
# MainWindow.Designer.cs
RaidCrawler.WinForms/MainWindow.Designer.cs
















## 增加身高体重和大小
### 三合一

```csharp

            // Initialize new controls
            LabelHeightWeightScale = new Label();
            HeightWeightScale = new TextBox();

            // Set properties for LabelHeightWeightScale
            LabelHeightWeightScale.AutoSize = true;
            LabelHeightWeightScale.Location = new Point(241, 324);
            LabelHeightWeightScale.Name = "LabelHeightWeightScale";
            LabelHeightWeightScale.Size = new Size(150, 15);
            LabelHeightWeightScale.Text = "Height/Weight/Scale";
            LabelHeightWeightScale.TextAlign = ContentAlignment.MiddleRight;

            // Set properties for HeightWeightScale
            HeightWeightScale.Location = new Point(400, 320);
            HeightWeightScale.Name = "HeightWeightScale";
            HeightWeightScale.Size = new Size(150, 23);
            HeightWeightScale.ReadOnly = true;

            //
            // MainWindow
            //
            // Add new controls to the form
            Controls.Add(LabelHeightWeightScale);
            Controls.Add(HeightWeightScale);




        private TextBox HeightWeightScale;
        private Label LabelHeightWeightScale;
```

### 尝试一
增加了三个文本框和三个标签控件，用于显示当前的身高、体重和大小。

```csharp
        private TextBox HeightSize;
        private TextBox WeightSize;
        private TextBox ScaleSize;
        private Label LabelHeight;
        private Label LabelWeight;
        private Label LabelScale;
```
```csharp
            // Initialize new controls
            HeightSize = new TextBox();
            WeightSize = new TextBox();
            ScaleSize = new TextBox();
            LabelHeight = new Label();
            LabelWeight = new Label();
            LabelScale = new Label();

            // Set properties for HeightSize
            HeightSize.Font = new Font("Consolas", 9F);
            HeightSize.Location = new Point(296, 320);
            HeightSize.Margin = new Padding(4, 3, 4, 3);
            HeightSize.Name = "HeightSize";
            HeightSize.ReadOnly = true;
            HeightSize.Size = new Size(95, 22);

            // Set properties for WeightSize
            WeightSize.Font = new Font("Consolas", 9F);
            WeightSize.Location = new Point(296, 348);
            WeightSize.Margin = new Padding(4, 3, 4, 3);
            WeightSize.Name = "WeightSize";
            WeightSize.ReadOnly = true;
            WeightSize.Size = new Size(95, 22);

            // Set properties for ScaleSize
            ScaleSize.Font = new Font("Consolas", 9F);
            ScaleSize.Location = new Point(296, 376);
            ScaleSize.Margin = new Padding(4, 3, 4, 3);
            ScaleSize.Name = "ScaleSize";
            ScaleSize.ReadOnly = true;
            ScaleSize.Size = new Size(95, 22);

            // Set properties for LabelHeight
            LabelHeight.AutoSize = true;
            LabelHeight.Location = new Point(241, 324);
            LabelHeight.Name = "LabelHeight";
            LabelHeight.Size = new Size(49, 15);
            LabelHeight.Text = "Height:";
            LabelHeight.TextAlign = ContentAlignment.MiddleRight;

            // Set properties for LabelWeight
            LabelWeight.AutoSize = true;
            LabelWeight.Location = new Point(241, 352);
            LabelWeight.Name = "LabelWeight";
            LabelWeight.Size = new Size(49, 15);
            LabelWeight.Text = "Weight:";
            LabelWeight.TextAlign = ContentAlignment.MiddleRight;

            // Set properties for LabelScale
            LabelScale.AutoSize = true;
            LabelScale.Location = new Point(241, 380);
            LabelScale.Name = "LabelScale";
            LabelScale.Size = new Size(39, 15);
            LabelScale.Text = "Scale:";
            LabelScale.TextAlign = ContentAlignment.MiddleRight;

                // Add new controls to the form
    Controls.Add(HeightSize);
    Controls.Add(WeightSize);
    Controls.Add(ScaleSize);
    Controls.Add(LabelHeight);
    Controls.Add(LabelWeight);
    Controls.Add(LabelScale);

    // Existing layout code...
```

### 尝试二

**计划：**
1. 将`HeightSize`、`WeightSize`和`ScaleSize`文本框放在同一行。
2. 使用`//`分隔每个文本框。

**代码：**

```csharp
// Initialize new controls
HeightSize = new TextBox();
WeightSize = new TextBox();
ScaleSize = new TextBox();
LabelHeight = new Label();
LabelWeight = new Label();
LabelScale = new Label();

// Set properties for HeightSize
HeightSize.Font = new Font("Consolas", 9F);
HeightSize.Location = new Point(296, 320);
HeightSize.Margin = new Padding(4, 3, 4, 3);
HeightSize.Name = "HeightSize";
HeightSize.ReadOnly = true;
HeightSize.Size = new Size(95, 22);

// Set properties for WeightSize
WeightSize.Font = new Font("Consolas", 9F);
WeightSize.Location = new Point(396, 320); // Adjusted location to be on the same line
WeightSize.Margin = new Padding(4, 3, 4, 3);
WeightSize.Name = "WeightSize";
WeightSize.ReadOnly = true;
WeightSize.Size = new Size(95, 22);

// Set properties for ScaleSize
ScaleSize.Font = new Font("Consolas", 9F);
ScaleSize.Location = new Point(496, 320); // Adjusted location to be on the same line
ScaleSize.Margin = new Padding(4, 3, 4, 3);
ScaleSize.Name = "ScaleSize";
ScaleSize.ReadOnly = true;
ScaleSize.Size = new Size(95, 22);

// Set properties for LabelHeight
LabelHeight.AutoSize = true;
LabelHeight.Location = new Point(241, 324);
LabelHeight.Name = "LabelHeight";
LabelHeight.Size = new Size(49, 15);
LabelHeight.Text = "Height:";
LabelHeight.TextAlign = ContentAlignment.MiddleRight;

// Set properties for LabelWeight
LabelWeight.AutoSize = true;
LabelWeight.Location = new Point(396, 324); // Adjusted location to be on the same line
LabelWeight.Name = "LabelWeight";
LabelWeight.Size = new Size(49, 15);
LabelWeight.Text = "Weight:";
LabelWeight.TextAlign = ContentAlignment.MiddleRight;

// Set properties for LabelScale
LabelScale.AutoSize = true;
LabelScale.Location = new Point(496, 324); // Adjusted location to be on the same line
LabelScale.Name = "LabelScale";
LabelScale.Size = new Size(39, 15);
LabelScale.Text = "Scale:";
LabelScale.TextAlign = ContentAlignment.MiddleRight;
```


