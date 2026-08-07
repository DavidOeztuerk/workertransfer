# Skill: Frontend Architecture (TypeScript 7, React 19, TanStack, Shadcn)

## Purpose
Build the WorkerTransfer frontend with modern React patterns, type-safe API integration, and a comprehensive design system.

## Tech Stack
- Node 24, TypeScript 7, Vite 7
- React 19, TanStack Router, TanStack Query
- Redux Toolkit + Zustand for state
- React Hook Form + Zod for forms
- Tailwind CSS + Shadcn/ui + Radix UI
- Motion for animations
- Storybook, Vitest, Playwright
- ESLint, Prettier, Biome

## Project Structure

```
apps/web/
├── package.json
├── tsconfig.json
├── vite.config.ts
├── tailwind.config.ts
├── .eslintrc.json
├── .prettierrc
├── biome.json
├── index.html
├── public/
│   └── favicon.ico
├── src/
│   ├── main.tsx                 # Entry point
│   ├── App.tsx                  # Root component
│   ├── routes/                  # TanStack Router routes
│   │   ├── __root.tsx
│   │   ├── index.tsx
│   │   ├── auth/
│   │   │   ├── login.tsx
│   │   │   ├── register.tsx
│   │   │   └── callback.tsx
│   │   ├── dashboard/
│   │   │   ├── index.tsx
│   │   │   ├── profile.tsx
│   │   │   ├── applications.tsx
│   │   │   └── transfers.tsx
│   │   ├── company/
│   │   │   ├── index.tsx
│   │   │   ├── jobs.tsx
│   │   │   ├── candidates.tsx
│   │   │   └── analytics.tsx
│   │   ├── ai/
│   │   │   ├── coach.tsx
│   │   │   ├── scout.tsx
│   │   │   └── negotiation.tsx
│   │   └── admin/
│   │       └── index.tsx
│   ├── features/                # Feature modules (domain-driven)
│   │   ├── identity/
│   │   ├── profile/
│   │   ├── resume/
│   │   ├── jobs/
│   │   ├── applications/
│   │   ├── transfers/
│   │   ├── contracts/
│   │   ├── messaging/
│   │   ├── notifications/
│   │   ├── search/
│   │   └── ai/
│   ├── shared/
│   │   ├── ui/                  # Design system (from packages/ui)
│   │   ├── components/          # Shared components
│   │   ├── hooks/               # Shared hooks
│   │   ├── utils/               # Utilities
│   │   ├── constants/           # Constants
│   │   └── types/               # Shared types
│   ├── api/                     # API layer (generated from OpenAPI)
│   │   ├── client.ts
│   │   ├── endpoints/
│   │   ├── types/
│   │   └── hooks/
│   ├── store/                   # Global state
│   │   ├── index.ts
│   │   ├── authStore.ts
│   │   ├── uiStore.ts
│   │   └── slices/
│   ├── styles/
│   │   ├── globals.css
│   │   ├── variables.css
│   │   └── tailwind.css
│   └── test/
│       ├── setup.ts
│       └── utils.tsx
└── stories/                     # Storybook stories
```

## Design System (packages/ui)

```typescript
// packages/ui/src/components/button.tsx
import { forwardRef, ButtonHTMLAttributes } from "react";
import { cn } from "@workertransfer/ui/utils";

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: "default" | "destructive" | "outline" | "secondary" | "ghost" | "link";
  size?: "default" | "sm" | "lg" | "icon";
  isLoading?: boolean;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = "default", size = "default", isLoading, disabled, children, ...props }, ref) => {
    return (
      <button
        ref={ref}
        className={cn(buttonVariants({ variant, size }), className)}
        disabled={disabled || isLoading}
        {...props}
      >
        {isLoading && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
        {children}
      </button>
    );
  }
);
Button.displayName = "Button";

// packages/ui/src/components/card.tsx
import { HTMLAttributes, forwardRef } from "react";
import { cn } from "@workertransfer/ui/utils";

export interface CardProps extends HTMLAttributes<HTMLDivElement> {}

export const Card = forwardRef<HTMLDivElement, CardProps>(
  ({ className, ...props }, ref) => (
    <div
      ref={ref}
      className={cn("rounded-lg border bg-card text-card-foreground shadow-sm", className)}
      {...props}
    />
  )
);
Card.displayName = "Card";

export const CardHeader = forwardRef<HTMLDivElement, HTMLAttributes<HTMLDivElement>>(
  ({ className, ...props }, ref) => (
    <div ref={ref} className={cn("flex flex-col space-y-1.5 p-6", className)} {...props} />
  )
);
CardHeader.displayName = "CardHeader";

export const CardTitle = forwardRef<HTMLParagraphElement, HTMLAttributes<HTMLHeadingElement>>(
  ({ className, ...props }, ref) => (
    <h3 ref={ref} className={cn("text-2xl font-semibold leading-none tracking-tight", className)} {...props} />
  )
);
CardTitle.displayName = "CardTitle";
```

## TanStack Router Setup

```typescript
// src/routes/__root.tsx
import { createRootRoute, Outlet, Link } from "@tanstack/react-router";
import { TanStackRouterDevtools } from "@tanstack/router-devtools";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "@workertransfer/ui/components/toaster";
import { AuthProvider } from "@/features/identity/authProvider";
import { Header } from "@/shared/components/Header";
import { Footer } from "@/shared/components/Footer";
import "@workertransfer/ui/styles.css";
import "./globals.css";

export const Route = createRootRoute({
  component: () => (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <div className="flex min-h-screen flex-col">
          <Header />
          <main className="flex-1">
            <Outlet />
          </main>
          <Footer />
        </div>
        <Toaster />
        <TanStackRouterDevtools />
      </AuthProvider>
    </QueryClientProvider>
  ),
});

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
});
```

## Type-Safe API Client

```typescript
// src/api/client.ts
import { createClient } from "@tanstack/react-query";
import type { paths, components } from "@/api/types";

export const api = createClient<paths>({
  baseUrl: import.meta.env.VITE_API_BASE_URL,
  headers: {
    "Content-Type": "application/json",
  },
  credentials: "include",
});

// Type-safe endpoint hooks
export function useGetUser(userId: string) {
  return api.useQuery("get", "/api/v1/users/{userId}", {
    params: { path: { userId } },
  });
}

export function useCreateApplication() {
  return api.useMutation("post", "/api/v1/applications", {
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["applications"] });
    },
  });
}

// Generated types from OpenAPI
// src/api/types.ts - generated by openapi-typescript
export interface paths {
  "/api/v1/users/{userId}": {
    get: {
      parameters: { path: { userId: string } };
      responses: { 200: { content: { "application/json": components["schemas"]["UserDTO"] } } };
    };
  };
  "/api/v1/applications": {
    post: {
      requestBody: { content: { "application/json": components["schemas"]["CreateApplicationRequest"] } };
      responses: { 201: { content: { "application/json": components["schemas"]["ApplicationDTO"] } } };
    };
  };
}
```

## Feature Module Pattern

```typescript
// src/features/profile/ProfileModule.tsx
import { createContext, useContext, useQuery, useMutation } from "@tanstack/react-query";
import { api } from "@/api/client";
import { ProfileForm } from "./components/ProfileForm";
import { ProfileView } from "./components/ProfileView";
import { useProfileStore } from "./store/profileStore";

export function ProfileProvider({ children }: { children: React.ReactNode }) {
  return (
    <ProfileContext.Provider value={{}}>
      {children}
    </ProfileContext.Provider>
  );
}

export function useProfile() {
  const context = useContext(ProfileContext);
  if (!context) throw new Error("useProfile must be used within ProfileProvider");
  return context;
}

// Components
export function ProfilePage() {
  const { data: profile } = useQuery({
    queryKey: ["profile", "me"],
    queryFn: () => api.get("/api/v1/profile/me"),
  });
  
  const updateMutation = useMutation({
    mutationFn: (data: ProfileUpdate) => api.put("/api/v1/profile/me", data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["profile"] }),
  });
  
  return profile ? (
    <ProfileView profile={profile} onUpdate={updateMutation.mutate} />
  ) : (
    <ProfileForm onSubmit={updateMutation.mutate} />
  );
}
```

## State Management

```typescript
// src/store/authStore.ts
import { create } from "zustand";
import { persist } from "zustand/middleware";
import type { User, TokenPair } from "@/api/types";

interface AuthState {
  user: User | null;
  accessToken: string | null;
  refreshToken: string | null;
  isAuthenticated: boolean;
  setAuth: (tokens: TokenPair, user: User) => void;
  clearAuth: () => void;
  updateUser: (user: User) => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
      setAuth: (tokens, user) => set({ ...tokens, user, isAuthenticated: true }),
      clearAuth: () => set({ user: null, accessToken: null, refreshToken: null, isAuthenticated: false }),
      updateUser: (user) => set((state) => ({ ...state, user })),
    }),
    { name: "auth-storage", partialize: (state) => ({ user: state.user, accessToken: state.accessToken, refreshToken: state.refreshToken }) }
  )
);

// src/store/slices/uiSlice.ts
import { createSlice, PayloadAction } from "@reduxjs/toolkit";

interface UIState {
  sidebarOpen: boolean;
  theme: "light" | "dark" | "system";
  notifications: Notification[];
}

const initialState: UIState = {
  sidebarOpen: true,
  theme: "system",
  notifications: [],
};

export const uiSlice = createSlice({
  name: "ui",
  initialState,
  reducers: {
    toggleSidebar: (state) => { state.sidebarOpen = !state.sidebarOpen; },
    setTheme: (state, action: PayloadAction<UIState["theme"]>) => { state.theme = action.payload; },
    addNotification: (state, action: PayloadAction<Notification>) => { state.notifications.push(action.payload); },
    removeNotification: (state, action: PayloadAction<string>) => { state.notifications = state.notifications.filter(n => n.id !== action.payload); },
  },
});
```

## Forms with React Hook Form + Zod

```typescript
// src/features/profile/components/ProfileForm.tsx
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Button } from "@workertransfer/ui";
import { Input } from "@workertransfer/ui/components/input";
import { Textarea } from "@workertransfer/ui/components/textarea";

const profileSchema = z.object({
  firstName: z.string().min(1, "First name is required").max(50),
  lastName: z.string().min(1, "Last name is required").max(50),
  headline: z.string().max(120).optional(),
  bio: z.string().max(2000).optional(),
  location: z.string().max(100).optional(),
  website: z.string().url("Invalid URL").optional().or(z.literal("")),
  linkedin: z.string().url("Invalid URL").optional().or(z.literal("")),
  github: z.string().url("Invalid URL").optional().or(z.literal("")),
});

type ProfileFormData = z.infer<typeof profileSchema>;

export function ProfileForm({ onSubmit }: { onSubmit: (data: ProfileFormData) => void }) {
  const form = useForm<ProfileFormData>({
    resolver: zodResolver(profileSchema),
    defaultValues: { firstName: "", lastName: "", headline: "", bio: "", location: "", website: "", linkedin: "", github: "" },
  });
  
  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-6">
      <div className="grid gap-4 md:grid-cols-2">
        <div>
          <label htmlFor="firstName" className="block text-sm font-medium mb-1">First Name</label>
          <Input id="firstName" {...form.register("firstName")} error={form.formState.errors.firstName?.message} />
        </div>
        <div>
          <label htmlFor="lastName" className="block text-sm font-medium mb-1">Last Name</label>
          <Input id="lastName" {...form.register("lastName")} error={form.formState.errors.lastName?.message} />
        </div>
      </div>
      
      <div>
        <label htmlFor="headline" className="block text-sm font-medium mb-1">Professional Headline</label>
        <Input id="headline" {...form.register("headline")} placeholder="Senior Software Engineer" />
      </div>
      
      <div>
        <label htmlFor="bio" className="block text-sm font-medium mb-1">Bio</label>
        <Textarea id="bio" {...form.register("bio")} rows={4} placeholder="Tell your story..." />
      </div>
      
      <div className="grid gap-4 md:grid-cols-3">
        <div>
          <label htmlFor="location" className="block text-sm font-medium mb-1">Location</label>
          <Input id="location" {...form.register("location")} placeholder="Berlin, Germany" />
        </div>
        <div>
          <label htmlFor="website" className="block text-sm font-medium mb-1">Website</label>
          <Input id="website" {...form.register("website")} type="url" placeholder="https://your-site.com" />
        </div>
        <div>
          <label htmlFor="github" className="block text-sm font-medium mb-1">GitHub</label>
          <Input id="github" {...form.register("github")} type="url" placeholder="https://github.com/username" />
        </div>
      </div>
      
      <Button type="submit" className="w-full" disabled={form.formState.isSubmitting}>
        Save Profile
      </Button>
    </form>
  );
}
```

## AI Integration Components

```typescript
// src/features/ai/components/AIChat.tsx
import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { api } from "@/api/client";
import { Button, Input, Card, CardContent } from "@workertransfer/ui";

interface Message {
  role: "user" | "assistant";
  content: string;
  agent?: string;
}

export function AIChat({ agentId }: { agentId: string }) {
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState("");
  
  const sendMutation = useMutation({
    mutationFn: (message: string) => api.post(`/api/v1/ai/agents/${agentId}/chat`, { message }),
    onSuccess: (response) => {
      setMessages((prev) => [...prev, { role: "assistant", content: response.data.content, agent: response.data.agent }]);
    },
  });
  
  const handleSend = (e: React.FormEvent) => {
    e.preventDefault();
    if (!input.trim()) return;
    setMessages((prev) => [...prev, { role: "user", content: input }]);
    sendMutation.mutate(input);
    setInput("");
  };
  
  return (
    <Card className="flex flex-col h-full">
      <CardContent className="flex-1 overflow-y-auto p-4 space-y-4">
        {messages.map((msg, i) => (
          <div key={i} className={`flex ${msg.role === "user" ? "justify-end" : "justify-start"}`}>
            <div className={`max-w-[70%] p-3 rounded-lg ${msg.role === "user" ? "bg-primary text-primary-foreground" : "bg-muted"}`}>
              {msg.agent && <span className="text-xs opacity-70 mb-1 block">{msg.agent}</span>}
              <p>{msg.content}</p>
            </div>
          </div>
        ))}
      </CardContent>
      <form onSubmit={handleSend} className="p-4 border-t">
        <div className="flex gap-2">
          <Input value={input} onChange={(e) => setInput(e.target.value)} placeholder="Ask your AI agent..." />
          <Button type="submit" disabled={sendMutation.isPending}>Send</Button>
        </div>
      </form>
    </Card>
  );
}
```

## Storybook Configuration

```typescript
// .storybook/main.ts
import type { StorybookConfig } from "@storybook/react-vite";

const config: StorybookConfig = {
  stories: ["../src/**/*.mdx", "../src/**/*.stories.@(js|jsx|mjs|ts|tsx)"],
  addons: [
    "@storybook/addon-links",
    "@storybook/addon-essentials",
    "@storybook/addon-interactions",
    "@storybook/addon-a11y",
  ],
  framework: {
    name: "@storybook/react-vite",
    options: {},
  },
  docs: { autodocs: "tag" },
  staticDirs: ["../public"],
  viteFinal: async (config) => {
    return config;
  },
};

export default config;
```

## Testing Setup

```typescript
// src/test/setup.ts
import { cleanup } from "@testing-library/react";
import { afterEach, vi } from "vitest";
import "@testing-library/jest-dom";

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

// Mock TanStack Query
vi.mock("@tanstack/react-query", () => ({
  useQuery: vi.fn(),
  useMutation: vi.fn(),
  QueryClient: vi.fn(),
  QueryClientProvider: ({ children }: { children: React.ReactNode }) => children,
}));

// Mock Router
vi.mock("@tanstack/react-router", () => ({
  useNavigate: () => vi.fn(),
  useParams: () => ({}),
  Link: ({ children, ...props }: any) => <a {...props}>{children}</a>,
}));
```

```typescript
// src/test/utils.tsx
import { render, RenderOptions } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ReactNode } from "react";

export function renderWithProviders(ui: ReactNode, options: RenderOptions = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  
  return render(
    <QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>,
    options
  );
}
```

## Build & Deploy

```bash
# Development
pnpm dev

# Type checking
pnpm check

# Linting
pnpm lint

# Testing
pnpm test
pnpm test:ui

# Storybook
pnpm storybook

# Build
pnpm build

# Preview build
pnpm preview
```