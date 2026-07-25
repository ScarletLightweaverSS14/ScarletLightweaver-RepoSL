# Barebones Component

The **Barebones Component** is the smallest usable SS14 ECS Component.

It exists to demonstrate the minimum amount of code required to create and register a Component.

---

# Overview

A **Component** stores **DATA**.

A **System** contains **LOGIC**.

Typical workflow:

```text
Create Component (.cs)
        ↓
Register Component
        ↓
Add Component to YAML
        ↓
Spawn an Entity
        ↓
Access it from a System
```

---

# Namespace

```csharp
namespace Content.Server._Starlight.LearningCSharp.Components;
```

A namespace is the class's address.

It usually matches the folder structure and allows C# to organize code.

Other files can reference this class through its namespace.

---

# Class Declaration

```csharp
public sealed partial class BarebonesComponent : Component
```

## `public`

Makes the class accessible from other code.

## `sealed`

Prevents other classes from inheriting from this one.

## `partial`

Allows this class to be split across multiple files.

## `class`

Defines a new class (a blueprint for creating objects).

## `: Component`

Inherits from the engine's base `Component` class.

This tells both C# and the engine:

> "This class **IS** a Component."

Without `: Component`, this would simply be a normal C# class.

---

# RegisterComponent

```csharp
[RegisterComponent]
```

Registers this Component with the engine.

Without this attribute:

- YAML cannot use this Component.
- The engine will not recognize it as an ECS Component.

---

# DataFields

```csharp
[DataField]
```

Allows YAML to override the field's default value.

Without `[DataField]`, the field always keeps its default value.

Example:

```csharp
[DataField]
public int ExampleValue = 5;
```

YAML:

```yml
components:
- type: ComponentTemplatePractice
  exampleValue: 10
```

Result:

```text
ExampleValue == 10
```

---

# Common Field Types

## `int`

Stores whole numbers.

Example:

```csharp
public int Value = 5;
```

---

## `float`

Stores decimal numbers.

Float literals use the `f` suffix.

Example:

```csharp
public float Value = 5.5f;
```

---

## `string`

Stores text.

Example:

```csharp
public string Name = "Example";
```

---

## `bool`

Stores either `true` or `false`.

Think of it like a switch:

- On / Off
- Yes / No
- Enabled / Disabled

Example:

```csharp
public bool Enabled = true;
```

or

```csharp
[DataField]
public bool CanExplode = false;
```

YAML:

```yml
components:
- type: Example
  canExplode: true
```

The entity can now explode because **YAML overrides the default value**.

The `[DataField]` attribute allows YAML to make that change.

---

# Next Step

After creating a Component, you will usually create a matching System.

```text
Components/
    ExampleComponent.cs

Systems/
    ExampleSystem.cs
```

The System contains the logic that reads and modifies the data stored inside the Component.
