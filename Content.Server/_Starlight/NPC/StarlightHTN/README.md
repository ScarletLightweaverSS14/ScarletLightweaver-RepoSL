# Starlight HTN Playground

## Purpose

This project exists to learn and experiment with SS14's Hierarchical Task Network (HTN) AI system.

Goals:
- Learn how SS14's HTN planner works.
- Understand every major HTN class.
- Build custom AI behaviours from scratch.
- Experiment with alternative AI designs.
- Document everything learned along the way.

This is not intended to replace SS14's HTN immediately.
It is a learning project that may eventually grow into a more flexible AI framework.

---

# HTN Codex

## Reading Checklist

Whenever I open a new HTN file, I should ask:

1. What is this object responsible for?
2. Who creates it?
3. Who owns it?
4. Who uses it?
5. When does it stop existing?

Lesson 0
✓ Server vs Client

Lesson 1
□ HTNComponent

Lesson 2
□ HTNSystem

Lesson 3
□ HTNPlan

Lesson 4
□ HTNTask

Lesson 5
□ Compound Tasks

Lesson 6
□ Primitive Tasks

Lesson 7
□ Operators

Lesson 8
□ Preconditions

Lesson 9
□ Effects

Lesson 10
□ Blackboard

Final Project
□ Wandering AI
□ Item Collector
□ Delivery AI
□ Custom Voidwalker AI

# Lesson 0 - Server vs Client

## Summary

Before learning how the HTN AI works, I learned how SS14 separates game logic between the server and the client.

### What I learned

- AI logic always runs on the server.
- Clients do **not** think for NPCs.
- Clients only receive the results of the server's decisions.
- The client HTN component mainly exists for debugging information.
- Running AI on every client would waste a large amount of CPU time.
- Running AI on the client could expose information that players should not know.
- The server is the authoritative source of truth.

### Key takeaway

Whenever I am looking for HTN logic, I should almost always be reading files under:

Content.Server/

not

Content.Client/

# Lesson 1 - ECS Ownership

## Summary

Before diving into HTN, I learned how ownership works in SS14's Entity Component System (ECS).

### What I learned

- An Entity is mostly just an identifier (EntityUid).
- Components own the data for a specific feature.
- Systems contain the logic that operates on Components.
- The HTNPlan belongs inside the HTNComponent because it is AI state.
- The HTNSystem updates the HTNComponent but does not own its data.

### Key takeaway

When reading SS14 code, I should ask:

- Is this data? → It probably belongs in a Component.
- Is this behavior or logic? → It probably belongs in a System.
- Is this just an object in the world? → That's an Entity.

# Lesson 2 - Operators Execute Tasks

## Summary

I learned how a Primitive Task is actually carried out.

### What I learned

- Primitive Tasks describe an action.
- Operators perform the actual work.
- Only the Operator knows whether its task has finished.
- When an Operator finishes, the HTNSystem advances to the next task in the current HTNPlan.

### Execution Flow

HTNPlan
    ↓
Current Primitive Task
    ↓
Operator executes
    ↓
Operator reports:
- Running
- Finished
- Failed
    ↓
HTNSystem decides what to do next

# Lesson 3 - Executing a Plan

## Summary

I learned how an HTNPlan is executed after it has been created.

### What I learned

- The HTNPlan stores a list of Primitive Tasks.
- The plan also stores an Index indicating the current task.
- When an Operator finishes successfully, the HTNSystem advances the Index.
- The next update uses the new Index to execute the next Primitive Task.
- Completed tasks usually remain in the list; the planner simply moves the Index forward.
- Using an Index is more efficient than removing tasks from the list.

### Example

Plan:

Index = 0

0 Walk
1 Open Door
2 Attack

Walk finishes.

↓

Index becomes 1.

↓

Current Task becomes "Open Door".

### Key takeaway

The HTNPlan behaves much like a checklist. Instead of deleting completed tasks, the AI simply advances to the next task by increasing the Index.

# Lesson 4 - Planning Never Stops

## Summary

I learned that an HTNPlan is temporary.

### What I learned

- A plan is only a temporary list of tasks.
- Once every task has finished, the plan is complete.
- The planner then creates a brand new plan based on the current world state.
- The planner is what gives the NPC intelligence.
- The HTNPlan is simply the result of the planner's decision.

### Mental Model

Planner
    ↓
Creates Plan
    ↓
Execute Plan
    ↓
Plan Finished
    ↓
Planner Creates New Plan

### Key takeaway

The HTN planner repeatedly creates new plans as the world changes. The plan itself is disposable and only represents the NPC's current course of action.

# Lesson 5 - Replanning

## Summary

I learned that an HTN AI does not simply execute one plan forever.

### What I learned

- Plans can become invalid as the world changes.
- The planner can discard an outdated plan and generate a new one.
- Replanning allows NPCs to react intelligently to changing situations.
- The planner is responsible for making decisions.
- Operators are responsible for carrying out those decisions.

### Planning Cycle

Observe World
    ↓
Create Plan
    ↓
Execute Plan
    ↓
World Changes
    ↓
Replan if necessary

### Key takeaway

A good HTN AI constantly evaluates whether its current plan is still appropriate instead of blindly following it to completion.

# Lesson 6 - When to Replan

## Summary

I learned that constantly replanning is not always a good idea.

### What I learned

- Replanning has a computational cost.
- Small changes in the world do not always require a new plan.
- Replanning too often can cause "plan thrashing," where an NPC repeatedly changes its mind and never finishes a task.
- A good HTN balances reacting to the world with completing its current objective.

### Good reasons to replan

- The current plan becomes impossible.
- The current goal changes.
- A much higher-priority objective appears.

### Key takeaway

An intelligent AI should not react to every small change. It should decide whether the change is important enough to justify creating a new plan.

# Lesson 7 - Objectives and Priority

## Summary

I realized that replanning alone is not enough. The AI also needs a way to determine whether a new objective is more important than the current one.

### What I learned

- Different objectives should have different priorities or scores.
- Replanning should not happen simply because a new objective exists.
- The AI should compare the importance of its current objective against new ones.
- Higher-priority objectives may interrupt the current plan.
- Lower-priority objectives should usually wait until the current plan is complete.

### Example

Current Objective:
Return Crystal (Priority 40)

New Objective:
Defend Ally (Priority 90)

The AI should interrupt its current plan because defending an ally is significantly more important.

### Design Idea

A separate Objective System could evaluate all available objectives, assign each a score, and choose the best one before the HTN planner generates a plan.

This separates **what** the NPC should do from **how** it should accomplish it.

### Key takeaway

HTNs are excellent at planning how to achieve a goal, but another system can be responsible for choosing which goal is most important.

# Lesson 8 - Separating Objectives from Planning

## Summary

I learned that deciding **what** an NPC should do is a separate problem from deciding **how** it should do it.

### What I learned

- The Objective System chooses the NPC's current goal.
- The HTN Planner creates a plan to achieve that goal.
- Primitive Tasks represent executable actions.
- Operators perform those actions in the game world.
- Separating objectives from planning creates a cleaner and more modular AI architecture.

### Example Architecture

World State
    ↓
Objective System
    ↓
Select Objective
    ↓
HTN Planner
    ↓
Generate HTN Plan
    ↓
Execute Primitive Tasks
    ↓
Operators

### Benefits

- Easier to add new objectives.
- Easier to create different personalities.
- The planner focuses only on achieving the chosen objective.
- The Objective System focuses only on selecting the most important goal.

### Design Note

A layered AI architecture is easier to maintain because each system has a single responsibility.

### Mental model

Entity
├── HTNComponent (AI data)
├── TransformComponent (position)
├── PhysicsComponent (physics)
└── HandsComponent (held items)

HTNSystem
    ↓
Reads and updates the HTNComponent every tick.

## Replanning Rules

An HTN should consider replanning when:

1. The current plan becomes impossible.
2. The current goal changes.
3. A significantly more important objective appears.

Small world changes usually should not trigger a full replan because excessive replanning can prevent the AI from ever finishing its tasks.
----------------------------------------------------------------------------------------------------

# Investigation Notes

These are observations from gameplay that I want to investigate while learning the HTN system.

## Observed Behaviour

### Movement

- NPCs sometimes appear to "hiccup" or pause during movement.
- NPCs can become stuck and stop acting.
- Hostile NPCs sometimes fail to reach their targets.

### Pathfinding

- Hostile NPCs sometimes fail to pathfind to enemies.
- NPCs sometimes appear to repeatedly choose poor paths.
- NPCs may walk in repetitive or predictable patterns.

### Combat

- Some hostile NPCs do not properly react after being attacked.
- NPCs may fail to switch targets when attacked.
- NPCs sometimes continue following an outdated plan.

### Hazards

- NPCs willingly walk onto landmines.
- NPCs appear to have little or no hazard awareness.

## Questions

- Is this an HTN problem?
- Is this a navigation/pathfinding problem?
- Is replanning happening often enough?
- Are Operators reporting failure correctly?
- Are Preconditions being checked correctly?
- Does the planner know about environmental hazards?
----------------------------------------------------------------------------------------------------







# Starlight HTN Development Roadmap

## Phase 1 - Learn Vanilla HTN

Goal:
Understand exactly how SS14's HTN works before changing its architecture.

Features:
- Learn every HTN class.
- Learn how planning works.
- Learn Operators.
- Learn Primitive Tasks.
- Learn Compound Tasks.
- Build simple example behaviours.
- Recreate small vanilla behaviours ourselves.

At the end of this phase I should be able to understand and modify any vanilla HTN.

---

## Phase 2 - Evaluate Vanilla

Goal:
Identify strengths and weaknesses of the current implementation.

Questions to answer:

- What works well?
- What feels overly complicated?
- What causes unnecessary replanning?
- What makes adding behaviours difficult?
- What should be redesigned?

---

## Phase 3 - Design Starlight HTN

Goal:
Design improvements while keeping compatibility where possible.

Possible improvements:

- Objective Layer
- Better scoring/priorities
- Memory improvements
- Personality system
- Cleaner debugging
- Easier task creation

---

## Phase 4 - Implement Improvements

Only after fully understanding vanilla HTN should architectural changes be introduced.

The goal is evolution rather than a complete rewrite.
-----------------------------------------

# Mission 1 - Foundations

## Goal

Understand the fundamental architecture that SS14's HTN AI is built upon before reading any implementation.

---

## Previous Knowledge

### Entity Component System (ECS)

Entity
- Represents an object in the world.
- Mostly acts as an identifier (EntityUid).

Component
- Stores data.
- Contains very little or no game logic.

System
- Contains behavior.
- Reads and modifies Components.

---

### Server vs Client

The server is authoritative.

Server:
- Runs AI.
- Runs physics.
- Makes gameplay decisions.

Client:
- Renders graphics.
- Displays debug information.
- Does not make gameplay decisions.

---

## What I Learned

### HTNPlan

- Stores the current plan created by the planner.
- Contains only Primitive Tasks.
- Tracks the current task using an Index.
- Is temporary and replaced when replanning.

---

### Primitive Tasks

- Represent executable actions.
- Are executed one at a time.
- Use Operators to perform work.

---

### Operators

- Perform the actual gameplay logic.
- Decide whether they are:
    - Running
    - Finished
    - Failed

---

### Replanning

An HTN does not execute one plan forever.

Cycle:

Observe World

↓

Create Plan

↓

Execute Plan

↓

World Changes

↓

Replan if necessary

---

### Objectives

A planner answers:

"How do I accomplish this?"

An Objective System would answer:

"What should I accomplish?"

Separating these responsibilities creates a cleaner architecture.

---

## Key Takeaways

- Components store data.
- Systems perform logic.
- HTNPlans are temporary.
- Operators execute gameplay actions.
- Replanning makes NPCs adaptive.
- Objectives and planning solve different problems.

# Mission 2 - Thinking in ECS

## Summary

Before reading HTNSystem.cs, I learned to predict how an ECS system should behave.

### What I learned

- Components store data.
- Systems contain logic.
- There is normally one System that processes many Components.
- NPCs should not update themselves.
- The HTNSystem is responsible for updating every HTNComponent.

### Mental Model

Many NPCs

↓

Many HTNComponents

↓

One HTNSystem

↓

Updates every HTNComponent

### Key takeaway

In ECS, Components own state while Systems process many Components. Rather than each NPC updating itself, one System updates all relevant entities.

# Mission 3 - The AI Loop

## Goal

Understand how the HTN system is updated every server tick.

---

## Previous Knowledge

- Entities are identifiers.
- Components store data.
- Systems contain logic.
- HTNPlans contain Primitive Tasks.
- Operators execute Primitive Tasks.

---

## New Questions

While reading HTNSystem.cs, answer these questions:

- How often does the HTN update?
- Who decides when to create a new plan?
- Who executes the current Operator?
- What happens when an Operator finishes?
- What happens when an Operator fails?

These questions will guide the investigation of the HTNSystem.
# Mission 3 - Thinking Like an ECS System

## What I Learned

A Component is passive.

It does not "run."

Instead:

- Systems read Components.
- Systems modify Components.
- Components simply store state.

For an HTNComponent this state includes things such as:

- Current Plan
- Blackboard
- Current Task
- AI State

The HTNSystem is responsible for updating this state every server tick.

### Mental Model

Server Tick

↓

HTNSystem

↓

Read HTNComponent

↓

Modify HTNComponent

↓

Next Tick

## Mission 3 - HTN Execution

### What I Learned

An EntityUid can exist without an HTNPlan.

Example:

EntityUid
├── HTNComponent
└── Other Components

HTNComponent

Plan = null

The HTNSystem is responsible for detecting this and generating a new plan.

---

HTN updates are incremental.

Instead of one NPC completing its entire plan before another NPC updates, the HTNSystem updates every NPC a little bit each server tick.

Conceptually:

Server Tick

↓

NPC1

↓

NPC2

↓

NPC3

↓

...

↓

NPC100

↓

Next Server Tick

This keeps AI responsive and distributes CPU usage evenly.

---

Operators are long-lived.

Many Operators take multiple server ticks to complete.

Example:

Tick 1
Walking...

↓

Tick 2
Walking...

↓

Tick 3
Walking...

↓

Tick 4
Destination reached

↓

Operator reports Finished.

### Operators Execute Over Time

Most Operators are **incremental**.

Instead of completing all of their work in one update, they perform a small amount of work each server tick.

Example:

Tick 1
Walking...

↓

Tick 2
Walking...

↓

Tick 3
Walking...

↓

Tick 4
Destination reached

↓

Finished

This allows NPCs to:

- React to changes while moving.
- Spread CPU usage across many ticks.
- Avoid freezing the server by performing too much work at once.

The HTNSystem repeatedly asks the current Operator whether it is:

- Running
- Finished
- Failed
