import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles.css'
import App from './App'

// Jellyfin injects the page HTML into its shell and then fires 'pageshow' on
// the page element. We defer React mounting until that event so that #root is
// guaranteed to be in the DOM and the Jellyfin globals (ApiClient, Dashboard)
// are fully initialised.
const page = document.querySelector('#NextUpFilterConfigPage')

function mount() {
  const root = document.getElementById('root')
  if (!root) return
  createRoot(root).render(
    <StrictMode>
      <App />
    </StrictMode>
  )
}

if (page) {
  page.addEventListener('pageshow', mount, { once: true })
} else {
  // Fallback for local dev (Vite dev server serves the full document directly).
  document.addEventListener('DOMContentLoaded', mount, { once: true })
}
