import type { LucideIcon } from '@lucide/angular';

/**
 * @lucide/angular v1 exports every icon as a standalone Angular *component*
 * (e.g. `LucideAlertTriangle`), not raw SVG node data like the deprecated
 * `lucide-angular` package did. `LucideIcon` (from the package) is the
 * exact TS type for these — a component type with a static `icon` data
 * property attached.
 *
 * Wherever an icon needs to be chosen at runtime (status maps, nav config,
 * component @Inputs), we store a reference to that component and render it
 * through the `LucideDynamicIcon` directive (selector `svg[lucideIcon]`):
 *   <svg [lucideIcon]="ref" class="h-4 w-4"></svg>
 * `LucideDynamicIcon` must be imported into each standalone component's
 * `imports` array wherever `[lucideIcon]` is used in its template — the
 * type below is for typing data only and is not itself renderable.
 */
export type IconRef = LucideIcon;
