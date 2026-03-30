import { useState, useRef, useEffect } from 'react'
import type { ExclusionItem, ItemType } from '../types'
import { TYPE_LABEL } from '../types'

interface Props {
  items: ExclusionItem[]
  onAdd: (item: ExclusionItem) => void
  onRemove: (id: string) => void
}

// Maps Jellyfin item types returned by the API to our ItemType discriminator.
const SUPPORTED_TYPES: Record<string, ItemType> = {
  Series:   'Series',
  Playlist: 'Playlist',
  BoxSet:   'BoxSet',
}

export function ExclusionSearch({ items, onAdd, onRemove }: Props) {
  const [query,   setQuery]   = useState('')
  const [results, setResults] = useState<JellyfinItem[]>([])
  const [open,    setOpen]    = useState(false)
  const debounce              = useRef<ReturnType<typeof setTimeout>>()
  const wrapRef               = useRef<HTMLDivElement>(null)

  useEffect(() => {
    function onMouseDown(e: MouseEvent) {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node))
        setOpen(false)
    }
    document.addEventListener('mousedown', onMouseDown)
    return () => document.removeEventListener('mousedown', onMouseDown)
  }, [])

  function handleInput(e: React.ChangeEvent<HTMLInputElement>) {
    const value = e.target.value
    setQuery(value)
    clearTimeout(debounce.current)
    if (value.trim().length < 2) { setOpen(false); return }

    debounce.current = setTimeout(() => {
      ApiClient.getItems(ApiClient.getCurrentUserId(), {
        IncludeItemTypes: 'Series,Playlist,BoxSet',
        Recursive:        true,
        SearchTerm:       value.trim(),
        Limit:            12,
        Fields:           'Id,Name,Type,ProductionYear',
      }).then(r => {
        // Only show types we can handle.
        setResults(r.Items.filter(i => i.Type && i.Type in SUPPORTED_TYPES))
        setOpen(true)
      })
    }, 300)
  }

  function pick(item: JellyfinItem) {
    const type = SUPPORTED_TYPES[item.Type!]
    if (!items.some(i => i.id === item.Id)) {
      onAdd({ type, id: item.Id, name: item.Name })
    }
    setQuery('')
    setOpen(false)
  }

  return (
    <>
      <div className="nuf-search-wrap" ref={wrapRef}>
        <input
          type="text"
          value={query}
          onChange={handleInput}
          onKeyDown={e => e.key === 'Escape' && setOpen(false)}
          placeholder="Search for a series, playlist, or collection…"
          autoComplete="off"
        />
        {open && (
          <div className="nuf-dropdown">
            {results.length === 0
              ? <div className="nuf-dropdown-item">No results</div>
              : results.map(item => (
                  <div
                    key={item.Id}
                    className="nuf-dropdown-item"
                    onMouseDown={() => pick(item)}
                  >
                    <span>{item.Name}</span>
                    <small>
                      {TYPE_LABEL[SUPPORTED_TYPES[item.Type!]]}
                      {item.ProductionYear ? `  •  ${item.ProductionYear}` : ''}
                    </small>
                  </div>
                ))
            }
          </div>
        )}
      </div>

      <div className="nuf-chip-list">
        {items.map(item => (
          <span key={item.id} className="nuf-chip">
            <span className="nuf-chip-type">{TYPE_LABEL[item.type]}</span>
            <span>{item.name}</span>
            <button type="button" onClick={() => onRemove(item.id)} title="Remove">×</button>
          </span>
        ))}
      </div>
    </>
  )
}
