// Type declarations for globals injected by the Jellyfin web client.
// These are available in the iframe context of a plugin configuration page.

interface JellyfinItem {
  Id: string
  Name: string
  ProductionYear?: number
  Type?: string
}

interface JellyfinUser {
  Id: string
  Name: string
}

interface JellyfinQueryResult {
  Items: JellyfinItem[]
  TotalRecordCount: number
}

interface JellyfinItemsQuery {
  IncludeItemTypes?: string
  Recursive?: boolean
  SearchTerm?: string
  Limit?: number
  Fields?: string
  Ids?: string
  ParentId?: string
}

declare const ApiClient: {
  getCurrentUserId(): string
  getUsers(): Promise<JellyfinUser[]>
  getItems(userId: string, params: JellyfinItemsQuery): Promise<JellyfinQueryResult>
  getPluginConfiguration(pluginId: string): Promise<Record<string, unknown>>
  updatePluginConfiguration(pluginId: string, config: Record<string, unknown>): Promise<unknown>
}

declare const Dashboard: {
  showLoadingMsg(): void
  hideLoadingMsg(): void
  processPluginConfigurationUpdateResult(result: unknown): void
}
