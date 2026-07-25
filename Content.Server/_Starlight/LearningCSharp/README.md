// ============================================================================
// COMPONENT TEMPLATE REFERENCE
// ============================================================================

/*
===============================================================================
OVERVIEW
===============================================================================

This is the smallest usable SS14 ECS Component.

A Component stores DATA.

A System contains LOGIC.

Typical workflow:

    Create Component (.cs)
            ↓
    Register Component
            ↓
    Add Component to YAML
            ↓
    Spawn an Entity
            ↓
    Access it from a System

===============================================================================
NAMESPACE
===============================================================================

namespace Content.Server._Starlight.LearningCSharp.Components;

The namespace is the class's address.

It usually matches the folder structure and allows C# to organize code.
Other files can reference this class through its namespace.

===============================================================================
CLASS DECLARATION
===============================================================================

public
    Makes the class accessible from other code.

sealed
    Prevents other classes from inheriting from this one.

partial
    Allows this class to be split across multiple files.

class
    Defines a new class (a blueprint for creating objects).

: Component
    Inherits from the engine's base Component class.

    This tells both C# and the engine:

        "This class IS a Component."

Without ": Component" this would simply be a normal C# class.

===============================================================================
REGISTER COMPONENT
===============================================================================

[RegisterComponent]

Registers this Component with the engine.

Without this attribute:

    - YAML cannot use this Component.
    - The engine will not recognize it as an ECS Component.

===============================================================================
DATA FIELDS
===============================================================================

[DataField]

Allows YAML to override the field's default value.

Without [DataField]:

    The field always keeps its default value.

Example:

    [DataField]
    public int ExampleValue = 5;

YAML:

    components:
    - type: ComponentTemplatePractice
      exampleValue: 10

Result:

    ExampleValue == 10

===============================================================================
COMMON FIELD TYPES
===============================================================================

int
    Whole numbers.

    Example:

        public int Value = 5;

float
    Decimal numbers.

    Float literals use the 'f' suffix.

    Example:

        public float Value = 5.5f;

string
    Text.

    Example:

        public string Name = "Example";

bool
    True or False. it can only have two values. like a switch. on and off.

    Example:

        public bool Enabled = true;
        or:
        [DataField]
        public bool CanExplode = false;
        In yaml:
        components:
        - type: Example
          enabled: true
Now, the entity CAN explode because YAML overrides the default value.
The [DataField] attribute allows YAML to make that change.
===============================================================================
NEXT STEP
===============================================================================

After creating a Component you usually create:

    Components/
        ExampleComponent.cs

    Systems/
        ExampleSystem.cs

The System contains the logic that reads and modifies the data stored inside
the Component.

*/
