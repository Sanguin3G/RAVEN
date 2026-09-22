import type { IdentityAmbiguityType, IdentityEntityType, IdentityOption, IdentityRelationshipToQuery } from "../../../types/identity";
import styles from "./identity.module.css";

interface IdentityChoiceListProps {
  entities: IdentityOption[];
  ambiguityType?: IdentityAmbiguityType;
  selectedEntityId?: string | null;
  onSelect: (temporaryId: string) => void;
  disabled?: boolean;
  ariaLabel?: string;
}

const entityOrder: Record<IdentityEntityType, number> = {
  ParentGroup: 0,
  Company: 1,
  Subsidiary: 2,
  Affiliate: 3,
  Brand: 4,
  Unknown: 5,
};

const relationshipOrder: Record<IdentityRelationshipToQuery, number> = {
  Exact: 0,
  Alias: 1,
  Parent: 2,
  Subsidiary: 3,
  SimilarName: 4,
  Possible: 5,
};

export function entityTypeLabel(value: IdentityEntityType) {
  switch (value) {
    case "ParentGroup": return "Parent group";
    case "Subsidiary": return "Subsidiary";
    case "Affiliate": return "Affiliate";
    case "Brand": return "Brand";
    case "Unknown": return "Organization";
    default: return "Company";
  }
}

function relationshipLabel(value: IdentityRelationshipToQuery) {
  switch (value) {
    case "Exact": return "Exact or direct match";
    case "Alias": return "Known alias";
    case "Parent": return "Parent of a related organization";
    case "Subsidiary": return "Related subsidiary";
    case "SimilarName": return "Similar name";
    default: return "Possible match";
  }
}

function compareEntities(left: IdentityOption, right: IdentityOption) {
  const typeDifference = entityOrder[left.entityType] - entityOrder[right.entityType];
  if (typeDifference !== 0) return typeDifference;
  const relationshipDifference = relationshipOrder[left.relationshipToQuery] - relationshipOrder[right.relationshipToQuery];
  if (relationshipDifference !== 0) return relationshipDifference;
  return left.displayName.localeCompare(right.displayName, undefined, { sensitivity: "base" });
}

/** Sorts a model-provided topology without trusting arbitrary array order. */
export function orderIdentityEntities(entities: IdentityOption[]) {
  const byId = new Map(entities.map((entity) => [entity.temporaryId, entity]));
  const childrenByParent = new Map<string, IdentityOption[]>();
  const roots: IdentityOption[] = [];

  for (const entity of entities) {
    const parent = entity.parentTemporaryId ? byId.get(entity.parentTemporaryId) : undefined;
    if (!parent || parent.temporaryId === entity.temporaryId) {
      roots.push(entity);
      continue;
    }
    const children = childrenByParent.get(parent.temporaryId) ?? [];
    children.push(entity);
    childrenByParent.set(parent.temporaryId, children);
  }

  const ordered: Array<{ entity: IdentityOption; depth: number }> = [];
  const visited = new Set<string>();
  const append = (entity: IdentityOption, depth: number) => {
    if (visited.has(entity.temporaryId)) return;
    visited.add(entity.temporaryId);
    ordered.push({ entity, depth });
    for (const child of (childrenByParent.get(entity.temporaryId) ?? []).sort(compareEntities)) append(child, depth + 1);
  };

  for (const root of roots.sort(compareEntities)) append(root, 0);
  // A malformed graph should not make an otherwise useful option disappear.
  for (const entity of [...entities].sort(compareEntities)) append(entity, 0);
  return ordered;
}

export function IdentityChoiceList({
  entities,
  ambiguityType,
  selectedEntityId,
  onSelect,
  disabled = false,
  ariaLabel = "Possible organizations",
}: IdentityChoiceListProps) {
  const orderedEntities = orderIdentityEntities(entities);
  if (orderedEntities.length === 0) return null;

  return (
    <div className={styles.choiceGroup} role="radiogroup" aria-label={ariaLabel}>
      {ambiguityType === "CorporateFamily" ? (
        <p className={styles.choiceHint}>The parent is shown first; related organizations are nested underneath it.</p>
      ) : null}
      <ul className={styles.choiceList}>
        {orderedEntities.map(({ entity, depth }) => {
          const inputId = `identity-option-${entity.temporaryId}`;
          const selected = selectedEntityId === entity.temporaryId;
          return (
            <li className={`${styles.choiceItem} ${depth > 0 ? styles.choiceItemNested : ""}`} key={entity.temporaryId}>
              <label className={`${styles.choiceCard} ${selected ? styles.choiceCardSelected : ""}`} htmlFor={inputId}>
                <input
                  id={inputId}
                  name="identity-option"
                  type="radio"
                  value={entity.temporaryId}
                  checked={selected}
                  onChange={() => onSelect(entity.temporaryId)}
                  disabled={disabled}
                />
                <span className={styles.choiceCopy}>
                  <span className={styles.choiceTitleRow}>
                    <strong>{entity.displayName}</strong>
                    {depth === 0 && entity.entityType === "ParentGroup" ? <span className={styles.choiceBadge}>Parent group</span> : null}
                  </span>
                  <span className={styles.choiceMeta}>
                    {entityTypeLabel(entity.entityType)} · {relationshipLabel(entity.relationshipToQuery)} · {entity.confidence} confidence
                  </span>
                  {entity.shortDescription ? <span className={styles.choiceDescription}>{entity.shortDescription}</span> : null}
                  <span className={styles.choiceDetails}>
                    {[entity.legalName, entity.country, entity.region, entity.officialDomain].filter(Boolean).join(" · ") || "Identity details are limited; public research will verify the target."}
                  </span>
                </span>
              </label>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
