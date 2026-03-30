import { useState, useEffect, useCallback } from 'react'
import { ExclusionSearch } from './components/ExclusionSearch'
import type { ExclusionItem, PluginConfig } from './types'
import { emptyUserSetting } from './types'

const PLUGIN_ID = 'a8e7d6c5-b4a3-4e2f-9d1b-c0e8f7a6d5b4'

export default function App() {
  const [users,          setUsers]          = useState<JellyfinUser[]>([])
  const [selectedUserId, setSelectedUserId] = useState('')
  const [items,          setItems]          = useState<ExclusionItem[]>([])
  const [fullConfig,     setFullConfig]     = useState<PluginConfig>({ UserSettings: [] })
  const [ready,          setReady]          = useState(false)

  // ── Initial load ────────────────────────────────────────────────────────────
  useEffect(() => {
    Dashboard.showLoadingMsg()

    const currentUserId = ApiClient.getCurrentUserId()

    Promise.all([
      ApiClient.getUsers(),
      ApiClient.getPluginConfiguration(PLUGIN_ID),
    ]).then(([userList, rawConfig]) => {
      const config = rawConfig as unknown as PluginConfig
      setUsers(userList)
      setFullConfig(config)

      const defaultId = userList.find(u => u.Id.toLowerCase() === currentUserId.toLowerCase())?.Id
        ?? userList[0]?.Id
        ?? ''

      setSelectedUserId(defaultId)
      applyUserConfig(defaultId, config)
    }).finally(() => {
      setReady(true)
      Dashboard.hideLoadingMsg()
    })
  }, [])

  // ── Load a user's config slice into local state ──────────────────────────────
  function applyUserConfig(userId: string, config: PluginConfig) {
    const key = userId.toLowerCase()
    const userSetting = config.UserSettings?.find(s => s.UserId === key)
      ?? emptyUserSetting(key)
    setItems(userSetting.ExclusionItems ?? [])
  }

  function handleUserChange(userId: string) {
    setSelectedUserId(userId)
    applyUserConfig(userId, fullConfig)
  }

  // ── Save ─────────────────────────────────────────────────────────────────────
  const handleSubmit = useCallback((e: React.FormEvent) => {
    e.preventDefault()
    Dashboard.showLoadingMsg()

    const key = selectedUserId.toLowerCase()
    const updatedSettings = [
      ...(fullConfig.UserSettings ?? []).filter(s => s.UserId !== key),
      { UserId: key, ExclusionItems: items },
    ]
    const updatedConfig: PluginConfig = {
      ...fullConfig,
      UserSettings: updatedSettings,
    }

    ApiClient.updatePluginConfiguration(PLUGIN_ID, updatedConfig as unknown as Record<string, unknown>)
      .then(result => {
        setFullConfig(updatedConfig)
        Dashboard.processPluginConfigurationUpdateResult(result)
      })
  }, [fullConfig, selectedUserId, items])

  if (!ready) return null

  return (
    <form onSubmit={handleSubmit}>

      {/* ── User picker ───────────────────────────────────────────────────── */}
      <div className="nuf-section">
        <div className="selectContainer">
          <label className="selectLabel" htmlFor="nuf-user-select">Configure for user</label>
          <select
            id="nuf-user-select"
            className="emby-select-withcolor emby-select"
            value={selectedUserId}
            onChange={e => handleUserChange(e.target.value)}
          >
            {users.map(u => (
              <option key={u.Id} value={u.Id}>{u.Name}</option>
            ))}
          </select>
        </div>
      </div>

      {/* ── Exclusion list ────────────────────────────────────────────────── */}
      <section className="nuf-section">
        <h2>Excluded from Next Up</h2>
        <p className="nuf-hint">
          Add series, playlists, or collections. Playlists and collections are
          resolved to their contained series at request time.
        </p>
        <ExclusionSearch
          items={items}
          onAdd={item => setItems(prev => [...prev, item])}
          onRemove={id => setItems(prev => prev.filter(i => i.id !== id))}
        />
      </section>

      <button type="submit" className="raised button-submit block emby-button">
        <span>Save</span>
      </button>

    </form>
  )
}
