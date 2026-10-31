import { Alert, Button, ConfigProvider, Empty, Form, Input, Select, Space, Spin, Table, Tag, Typography, theme } from 'antd';
import { useState } from 'react';
import type { Commit, Provider, Registration } from './api';
import { useRepositories } from './useRepositories';
import './style.css';
export default function App() {
  const state = useRepositories();
  const [provider, setProvider] = useState<Provider>('GitHub');
  const [form] = Form.useForm<Registration>();
  return <ConfigProvider theme={{ algorithm: theme.darkAlgorithm, token: { colorPrimary: '#62b7ff', borderRadius: 6 } }}>
    <main><header><Typography.Title level={1}>DevPulse</Typography.Title><Typography.Paragraph>Register an authorized repository and inspect its latest real commits.</Typography.Paragraph></header>
      <section aria-labelledby="registration-heading"><Typography.Title level={2} id="registration-heading">Register repository</Typography.Title>
        {state.aliasError && <Alert type="warning" title={state.aliasError} showIcon />}
        <Form form={form} layout="vertical" initialValues={{ provider: 'GitHub' }} onFinish={values => void state.load(values)}>
          <Form.Item name="provider" label="Provider" rules={[{ required: true }]}><Select onChange={(value: Provider) => { setProvider(value); form.resetFields(['owner', 'name', 'alias']); }} options={[{ value: 'GitHub', label: 'GitHub' }, { value: 'LocalGit', label: 'Local Git' }]} /></Form.Item>
          {provider === 'GitHub' ? <div className="identity-fields"><Form.Item name="owner" label="Owner" preserve={false} rules={[{ required: true }, { pattern: /^[a-zA-Z0-9][a-zA-Z0-9-]{0,38}$/, message: 'Enter a valid GitHub owner.' }]}><Input autoComplete="off" /></Form.Item><Form.Item name="name" label="Repository name" preserve={false} rules={[{ required: true }, { max: 100 }, { pattern: /^(?!\.{1,2}$)[a-zA-Z0-9_.][a-zA-Z0-9_.-]*$/, message: 'Enter a valid repository name.' }]}><Input autoComplete="off" /></Form.Item></div> : <Form.Item name="alias" label="Approved local repository" preserve={false} rules={[{ required: true }]}><Select placeholder="Choose an administrator-approved alias" options={state.aliases.map(item => ({ value: item.alias, label: item.name }))} notFoundContent="No local repositories have been approved." /></Form.Item>}
          <Button htmlType="submit" type="primary" loading={state.busy}>Register and load commits</Button>
        </Form>
      </section>
      <section aria-labelledby="history-heading"><Space className="history-toolbar" wrap><Typography.Title level={2} id="history-heading">Commit history</Typography.Title>{state.repositories.length > 0 && <Select aria-label="Registered repository" value={state.selected?.id} placeholder="Select a registered repository" options={state.repositories.map(item => ({ value: item.id, label: item.name }))} onChange={id => { const repository = state.repositories.find(item => item.id === id); if (repository) void state.load(repository); }} />}</Space>
        {state.error && <Alert role="alert" type="error" title={state.error} showIcon />}
        {state.selected && <Typography.Paragraph><strong>{state.selected.name}</strong> <Tag>{state.selected.provider}</Tag> {state.selected.scope} · Latest 20 commits <Button size="small" onClick={() => void state.load(state.selected!)} disabled={state.busy}>Refresh</Button></Typography.Paragraph>}
        <div aria-live="polite" aria-busy={state.busy}>{state.busy ? <div className="loading"><Spin /><span>Loading repository commits…</span></div> : state.history ? <Table<Commit> rowKey="sha" size="small" pagination={false} dataSource={state.history.commits} scroll={{ x: 640 }} locale={{ emptyText: 'This repository has no commits.' }} columns={[
          { title: 'SHA', dataIndex: 'sha', width: 130, render: (sha: string) => <Typography.Text code copyable={{ text: sha }} title={sha}>{sha.slice(0, 8)}</Typography.Text> },
          { title: 'Commit', dataIndex: 'message', render: (message: string) => <span className="commit-message">{message}</span> },
          { title: 'Author', dataIndex: 'author', width: 160 },
          { title: 'Authored', dataIndex: 'authoredAt', width: 210, render: (date: string) => <time dateTime={date} title={date}>{new Date(date).toLocaleString()}</time> },
        ]} /> : !state.error && <Empty description="Register a repository to load its commit history." />}</div>
      </section><footer>Single-owner interface · Public GitHub repositories and explicitly authorized private repositories · Local aliases are managed by the administrator</footer>
    </main>
  </ConfigProvider>;
}
