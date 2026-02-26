# Graph Database & Gremlin (Cosmos DB)

**Duration:** 30 Hours  

---

## Day 1 (3 Hours) – Introduction to Graph Databases & Property Graph Model

- What is a Graph Database  
- Why Graph Databases for connected data  
- Comparison: Graph vs Relational vs NoSQL  
- Real-world use cases  
  - IoT systems  
  - Asset management  
  - Dependency relationships  
- Introduction to Azure Cosmos DB  
- Overview of Cosmos DB APIs  
- Where Gremlin API fits  
- **Demo:** Visualizing simple graph concepts (vertices & edges)  
- Property Graph Model explained  
- Vertices, edges, labels, properties  
- Directionality of relationships  
- Multiple edges between vertices  
- Schema-optional nature of graph databases  
- Modeling implications  
- **Demo:** Simple property graph structure in Cosmos DB (viewer + queries)  

---

## Day 2 (3 Hours) – Gremlin Fundamentals & Core Traversal Patterns

- What is Gremlin  
- Gremlin as a traversal language  
- Graph traversal mindset vs SQL querying  
- Gremlin syntax basics  
  - `g.V()`, `g.E()`  
  - `has()`, `hasLabel()`, `values()`  
- **Demo:** Basic vertex and edge queries  
- `out()`, `in()`, `both()`  
- Chaining traversals  
- Filtering patterns  
- Projection basics  
- Path traversal concepts  
- Introduction to traversal flow  
- **Demo:** Relationship traversal examples  

---

## Day 3 (3 Hours) – Traversal Strategies & Evaluation Model

- How Gremlin traversals are evaluated  
- Lazy evaluation concept  
- Traverser lifecycle (high-level)  
- Barriers and steps (conceptual)  
- Cosmos DB Gremlin execution overview  
- Differences from reference Gremlin behavior  
- **Demo:** Query execution behavior observation  
- IoT entities as graph nodes  
- Assets, gateways, sensors, units  
- Hierarchical vs network relationships  

---

## Day 4 (3 Hours) – Modeling IoT & Asset Relationships

- Parent-child vs peer relationships  
- Labeling strategies  
- **Demo:** Asset hierarchy graph model  
- Many-to-many relationships  
- Contextual metadata on edges  
- Relationship versioning (conceptual)  
- Modeling trade-offs  
- When to denormalize  
- **Demo:** IoT relationship patterns in Gremlin  

---

## Day 5 (3 Hours) – Intermediate Gremlin Traversals

- Conditional traversals  
- Hierarchical traversals  
- Path-based queries  
- Subgraph concepts  
- Introduction to `repeat()`, `emit()`, `until()`  
- **Demo:** Hierarchy traversal patterns  

---

## Day 6 (3 Hours) – Advanced Data Shaping & Aggregations

- Projection techniques  
- `group()` and `groupCount()`  
- `count()`, `fold()`, `unfold()`  
- Aggregation patterns for asset data  
- Shaping output for applications  
- **Demo:** Aggregation and summary queries  

---

## Day 7 (3 Hours) – Query Optimization & Performance (Part 1)

- Understanding RU consumption  
- Avoiding full graph scans  
- Importance of partition keys  
- Partition-aware query patterns  
- Early filtering strategies  
- **Demo:** Comparing efficient vs inefficient queries  

---

## Day 8 (3 Hours) – Query Optimization & Performance (Part 2)

- Reducing projection cost  
- Deep traversal mitigation strategies  
- `repeat()` & `emit()` usage guidelines  
- Edge explosion problem  
- `range()`, `limit()`, and `order()` tuning  
- **Demo:** Performance-tuned traversal examples  

---

## Day 9 (3 Hours) – Monitoring, Best Practices & Migration Strategies

- Monitoring RU consumption  
- Reading query metrics  
- Using pre-computed values  
- Common Gremlin pitfalls  
- Error handling patterns  
- Safe query design principles  
- **Demo:** RU measurement and query refinement  
- Graph-to-Graph migration concepts  
- Preserving relationships  
- Vertex and edge sequencing  
- ID consistency strategies  
- Relational / NoSQL to Graph migration  
- Identifying natural edges  
- **Demo:** Conceptual migration flow with examples  

---

## Day 10 (3 Hours) – End-to-End Use Cases & Labs

- Fetch all gateways for a tenant (pagination impact)  
- Retrieve full asset hierarchy (ordered & paginated)  
- Retrieve equipment with assigned sensors  
- Compute health summary for asset hierarchy  
- Detect orphaned sensors  
- Optimized metadata retrieval  
- Count equipment under a unit  
- Validate hierarchy consistency  
- Performance debugging demo  
- **Demo:** Use case implementations and advanced patterns  
- Course wrap-up & Q&A  