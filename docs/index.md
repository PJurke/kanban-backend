# Kanban API

> Kanban visualizes the flow of cards through defined states to improve decisions about progress, capacity, and priority.

**Universal method.** Kanban is a universal method. It can be used for managing support requests, keeping an overview of the product lifecycle, bringing flow to personal daily work, etc.


## Elements
Because of this universality, we need to use terms that cover the potential use cases.

### Board
A board is a collection of columns and cards.

### Item Name
The item in the Kanban board which flows through the system. There are multiple candidates, like
- **Card**<br>
  A neutral container that represents something moving through states. It makes no assumptions about whether this is work, value, a product, or a lifecycle object, which makes it universally applicable.
- **Flow Item**<br>
  Emphasizes the movement through a system rather than the thing itself. Conceptually strong, but abstract and harder to grasp for beginners or non-theoretical audiences.
- **Issue**<br>
  Strongly associated with problems, defects, or tickets. This makes it unsuitable for neutral or positive subjects like products, ideas, or lifecycle phases.
- **Kanban Item**<br>
  Clearly ties the element to the Kanban method or tool. This limits universality and frames Kanban as a framework rather than a general flow system.
- **Value Item**<br>
  Assumes that everything represented creates value. This is aspirational but unrealistic, as many necessary items (risk, maintenance, learning) are not directly value-adding.
- **Work Item**<br>
  Works well when Kanban is used to manage tasks or activities. It breaks down when representing passive objects, long-running entities, or lifecycle states beyond “work.”

I decided to use the term **Card**.

## Data Structures

### Cards
A card can be everything, like a task, a user story, a support request, a feature, a product, etc. Each of these items has its own properties, but they all share some common properties. The system must allow the user to define the properties of a type.

## Requirements

### Visualize

1. **Work Item Types:** The application must allow the diverse types of work that can flow through the system. This includes tasks, bugs, user stories, and more.
2. **Classes of Services:** The application must allow the diverse classes of services that can flow through the system. This includes internal and external services.

### WIP

1. **Column Limits:** Each column (or column group) must have a limit on the number of work items that can be in it. It shall be a soft limit, meaning that the system can exceed it, but it shall notify the user about it.

### Manage Flow

1. **Measure Waiting Times and Active Times:** The application must allow the user to measure the waiting times and active times of the work items.
2. **Status History:** The application must allow the user to see the history of the work items.